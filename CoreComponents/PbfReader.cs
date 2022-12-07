using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using OsmSharp.Complete;
using OsmSharp.Tags;
using ProtoBuf;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace PraxisCore.PbfReader
{
    public record struct IndexInfo(int blockId, int groupId, byte groupType, long minId, long maxId); //trying this as a struct. 22 bytes instead of 16 as suggested, but about 4% faster.
    /// <summary>
    /// PraxisMapper's customized, multithreaded PBF parser. Saves on RAM usage by relying on disk access when needed. Can resume a previous session if stopped for some reason.
    /// </summary>
    public class PbfReader
    {
        //The 6th generation of logic for pulling geometry out of a pbf file. This one is written specfically for PraxisMapper, and
        //doesn't depend on OsmSharp for reading the raw data now. OsmSharp's still used for object types now that there's our own
        //FeatureInterpreter instead of theirs. 

        static int initialCapacity = 8009; //ConcurrentDictionary says initial capacity shouldn't be divisible by a small prime number, so i picked the prime closes to 8,000 for initial capacity
        static int initialConcurrency = Environment.ProcessorCount;

        public bool saveToDB = false;
        public bool onlyMatchedAreas = false; //if true, only process geometry if the tags come back with IsGamplayElement== true;
        public string processingMode = "normal"; //normal: use geometry as it exists. Center: save the center point of any geometry provided instead of its actual value.
        public string styleSet = "mapTiles"; //which style set to use when parsing entries
        public bool keepIndexFiles = true;
        public bool splitByStyleSet = false;

        public string outputPath = "";
        public string filenameHeader = "";

        public bool lowResourceMode = false;
        public bool reprocessFile = false; //if true, we load TSV data from a previous run and re-process that by the rules.
        public bool keepAllBlocksInRam = false; //if true, keep all decompressed blocks in memory instead of purging out unused ones each block.

        FileInfo fi;
        FileStream fs; // The input file. Output files are either WriteAllText or their own streamwriter.

        //new entries for indexing.
        static List<IndexInfo> nodeIndex = new List<IndexInfo>();
        static List<IndexInfo> wayIndex = new List<IndexInfo>();
        static List<IndexInfo> relationIndex = new List<IndexInfo>();

        Dictionary<long, long> blockPositions = new Dictionary<long, long>(initialCapacity);
        Dictionary<long, int> blockSizes = new Dictionary<long, int>(initialCapacity);

        Envelope bounds = null; //If not null, reject elements not within it
        IPreparedGeometry boundsEntry = null; //use for precise detection of what to include.

        ConcurrentDictionary<long, PrimitiveBlock> activeBlocks = new ConcurrentDictionary<long, PrimitiveBlock>(initialConcurrency, initialCapacity);
        ConcurrentDictionary<long, bool> accessedBlocks = new ConcurrentDictionary<long, bool>(initialConcurrency, initialCapacity);

        int nextBlockId = 0;
        long firstWayBlock = 0;
        int startNodeBtreeIndex = 0; //Only set, not read from.
        int startWayBtreeIndex = 0;
        int nodeHintsMax = 12;

        int nodeIndexEntries = 0;
        int wayIndexEntries = 0;
        int relationIndexEntries = 0;
        int startRelationBtreeIndex = 0;

        ConcurrentBag<Task> relList = new ConcurrentBag<Task>(); //Individual, smaller tasks.
        ConcurrentBag<TimeSpan> timeListRelations = new ConcurrentBag<TimeSpan>(); //how long each Group took to process.
        ConcurrentBag<TimeSpan> timeListWays = new ConcurrentBag<TimeSpan>(); //how long each Group took to process.

        long nodesProcessed = 0;
        long totalProcessEntries = 0;

        public bool displayStatus = true;

        CancellationTokenSource tokensource = new CancellationTokenSource();
        CancellationToken token;

        object nodeLock = new object();

        string[] currentBlockStringTable;

        public PbfReader()
        {
            token = tokensource.Token;
            Serializer.PrepareSerializer<PrimitiveBlock>();
            Serializer.PrepareSerializer<Blob>();
        }

        /// <summary>
        /// Returns how many blocks are in the current PBF file.
        /// </summary>
        /// <returns>long of blocks in the opened file</returns>
        public int BlockCount()
        {
            return blockPositions.Count;
        }

        /// <summary>
        /// Opens up a file for reading. 
        /// </summary>
        /// <param name="filename">the path to the file to read.</param>
        private void Open(string filename)
        {
            fi = new FileInfo(filename);
            fs = File.OpenRead(filename);
        }

        /// <summary>
        /// Closes the currently open file
        /// </summary>
        private void Close()
        {
            fs.Close();
            fs.Dispose();
            tokensource.Cancel();
        }

        private void ReprocessFileToCenters(string filename)
        {
            //load up each line of a file from a previous run, and then re-process it according to the current settings.
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            Log.WriteLog("Loading " + filename + " for processing at " + DateTime.Now);
            var fr = File.OpenRead(filename);
            var sr = new StreamReader(fr);
            sw.Start();
            var reprocFileStream = new StreamWriter(new FileStream(outputPath + filenameHeader + Path.GetFileNameWithoutExtension(filename) + "-reprocessed.geomData", FileMode.OpenOrCreate));

            while (!sr.EndOfStream)
            {
                StringBuilder sb = new StringBuilder();
                string entry = sr.ReadLine();
                DbTables.Place md = GeometrySupport.ConvertSingleTsvPlace(entry);

                if (bounds != null && (!bounds.Intersects(md.ElementGeometry.EnvelopeInternal)))
                    continue;

                if (processingMode == "center")
                    md.ElementGeometry = md.ElementGeometry.Centroid;

                sb.Append(md.SourceItemID).Append('\t').Append(md.SourceItemType).Append('\t').Append(md.ElementGeometry.AsText()).Append('\t').Append(md.AreaSize).Append('\t').Append(md.PrivacyId).Append("\r\n");
                reprocFileStream.WriteLine(sb.ToString());
            }
            sr.Close(); sr.Dispose(); fr.Close(); fr.Dispose();
            reprocFileStream.Close(); reprocFileStream.Dispose();
        }

        /// <summary>
        /// Runs through the entire process to convert a PBF file into usable PraxisMapper data. The server bounds for this process must be identified via other functions.
        /// </summary>
        /// <param name="filename">The path to the PBF file to read</param>
        /// <param name="onlyTagMatchedEntries">If true, only load data in the file that meets a rule in TagParser. If false, processes all elements in the file.</param>
        public void ProcessFile(string filename, long relationId = 0)
        {
            try
            {
                if (reprocessFile)
                {
                    ReprocessFileToCenters(filename);
                    return;
                }

                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();

                PrepareFile(filename);

                filenameHeader += styleSet + "-";

                if (relationId != 0)
                {
                    filenameHeader += relationId.ToString() + "-";
                    //Get the source relation first
                    var relation = MakeCompleteRelation(relationId);
                    var NTSrelation = GeometrySupport.ConvertOsmEntryToPlace(relation);
                    bounds = NTSrelation.ElementGeometry.EnvelopeInternal;
                    var pgf = new PreparedGeometryFactory();
                    boundsEntry = pgf.Create(NTSrelation.ElementGeometry);
                }

                int nextGroup = FindLastCompletedGroup() + 1; //saves -1 at the start of a block, so add this to 0.

                if (!lowResourceMode) //typical path
                {
                    for (var block = nextBlockId; block >= firstWayBlock; block--)
                    {
                        var blockData = GetBlock(block);
                        if (nextGroup >= blockData.primitivegroup.Count)
                            nextGroup = 0;
                        for (int i = nextGroup; i < blockData.primitivegroup.Count; i++)
                        {
                            try
                            {
                                var group = blockData.primitivegroup[i];
                                System.Diagnostics.Stopwatch swGroup = new System.Diagnostics.Stopwatch();
                                swGroup.Start();
                                int thisBlockId = block;
                                var geoData = GetGeometryFromGroup(thisBlockId, group, onlyMatchedAreas);
                                //There are large relation blocks where you can see how much time is spent writing them or waiting for one entry to
                                //process as the apps drops to a single thread in use, but I can't do much about those if I want to be able to resume a process.
                                if (geoData != null) //This process function is sufficiently parallel that I don't want to throw it off to a Task. The only sequential part is writing the data to the file, and I need that to keep accurate track of which blocks have beeen written to the file.
                                {
                                    ProcessReaderResults(geoData, block, i);
                                }
                                SaveCurrentBlockAndGroup(block - 1, i);
                                swGroup.Stop();
                                if (group.relations.Count > 0)
                                    timeListRelations.Add(swGroup.Elapsed);
                                else
                                    timeListWays.Add(swGroup.Elapsed);
                                Log.WriteLog("Block " + block + " Group " + i + " processed in " + swGroup.Elapsed);
                            }
                            catch
                            {
                                Log.WriteLog("Failed to process block " + block + " normally, trying the low-resourse option", Log.VerbosityLevels.Errors);
                                Thread.Sleep(3000); //Not necessary, but I want to give the GC a chance to clean up stuff before we pick back up.
                                LastChanceRead(block);
                            }
                        }
                        nextGroup = 0;
                        SaveCurrentBlockAndGroup(block - 1, -1); //save last completed group, not next group to run, so make this -1 at end of a block.
                    }
                    Log.WriteLog("Processing all node blocks....");
                    ProcessAllNodeBlocks(firstWayBlock);
                }
                else
                {
                    //low resource mode
                    //run each entry one at a time, save to disk immediately, don't multithread.
                    for (var block = nextBlockId; block > 0; block--)
                    {
                        LastChanceRead(block);
                    }
                }
                Close();
                CleanupFiles();
                sw.Stop();
                Log.WriteLog("File completed at " + DateTime.Now + ", session lasted " + sw.Elapsed);
            }
            catch (Exception ex)
            {
                while (ex.InnerException != null)
                    ex = ex.InnerException;
                Log.WriteLog("Error processing file: " + ex.Message + ex.StackTrace);
            }
        }

        public void LastChanceRead(long block)
        {
            var thisBlock = GetBlock(block);
            var nextgroup = FindLastCompletedGroup() + 1;
            for (int i = nextgroup; i < thisBlock.primitivegroup.Count; i++)
            {
                var group = thisBlock.primitivegroup[i];
                var geoListOfOne = new List<ICompleteOsmGeo>();
                if (group.relations.Count > 0)
                {
                    foreach (var relId in group.relations)
                    {
                        Log.WriteLog("Loading relation with " + relId.memids.Count + " members");
                        geoListOfOne.Add(MakeCompleteRelation(relId.id, onlyMatchedAreas, thisBlock));
                        ProcessReaderResults(geoListOfOne, block, i);
                        activeBlocks.Clear();
                        geoListOfOne.Clear();
                    }
                }
                else if (group.ways.Count > 0)
                {
                    foreach (var wayId in group.ways)
                    {
                        geoListOfOne.Add(MakeCompleteWay(wayId.id, -1, onlyMatchedAreas));
                        ProcessReaderResults(geoListOfOne, block, i);
                        activeBlocks.Clear();
                        geoListOfOne.Clear();
                    }
                }
                else if (group.nodes.Count > 0)
                {
                    var nodes = GetTaggedNodesFromBlock(thisBlock, onlyMatchedAreas);
                    ProcessReaderResults(nodes, block, i);
                }
                SaveCurrentBlockAndGroup(block, i);
            }
        }

        public void ProcessAllNodeBlocks(long maxNodeBlock)
        {
            //Throw each node block into its own thread.
            Parallel.For(1, maxNodeBlock, (block) => //parallel here dies on planet.osm.pbf. Moved to make groups parallel.
            {
                var blockData = GetBlock(block);
                var geoData = GetTaggedNodesFromBlock(blockData, onlyMatchedAreas);
                if (geoData != null)
                    ProcessReaderResults(geoData, block, 0);

                activeBlocks.TryRemove(block, out blockData);
                blockData = null;
            });
        }

        private void IndexFile()
        {
            Log.WriteLog("Indexing file...");
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            fs.Position = 0;
            int blockCounter = 0;
            blockPositions = new Dictionary<long, long>(initialCapacity);
            blockSizes = new Dictionary<long, int>(initialCapacity);
            ConcurrentBag<IndexInfo> indexInfos = new ConcurrentBag<IndexInfo>();

            BlobHeader bh = new BlobHeader();
            Blob b = new Blob();

            HeaderBlock hb = new HeaderBlock();
            PrimitiveBlock pb = new PrimitiveBlock();

            //Only one OsmHeader, at the start
            Serializer.MergeWithLengthPrefix(fs, bh, PrefixStyle.Fixed32BigEndian);
            hb = Serializer.Deserialize<HeaderBlock>(fs, length: bh.datasize); //only one of these per file    
            blockPositions.Add(0, fs.Position);
            blockSizes.Add(0, bh.datasize);

            List<Task> waiting = new List<Task>(initialCapacity);
            int relationCounter = 0;
            int wayCounter = 0;

            //header block is 0, start data blocks at 1
            while (fs.Position != fs.Length)
            {
                blockCounter++;
                Serializer.MergeWithLengthPrefix(fs, bh, PrefixStyle.Fixed32BigEndian);
                blockPositions.Add(blockCounter, fs.Position);
                blockSizes.Add(blockCounter, bh.datasize);

                byte[] thisblob = new byte[bh.datasize];
                fs.Read(thisblob, 0, bh.datasize);

                var passedBC = blockCounter;
                var tasked = Task.Run(() => //Threading makes this run approx. twice as fast.
                {
                    var pb2 = DecodeBlock(thisblob);

                    for (int i = 0; i < pb2.primitivegroup.Count; i++) //Planet.osm uses several primitivegroups per block, extracts usually use one.
                    {
                        var group = pb2.primitivegroup[i];
                        {
                            if (group.ways.Count > 0)
                            {
                                wayCounter++;
                                indexInfos.Add(new IndexInfo(passedBC, i, 2, group.ways.First().id, group.ways.Last().id));
                            }
                            else if (group.relations.Count > 0)
                            {
                                relationCounter++;
                                indexInfos.Add(new IndexInfo(passedBC, i, 3, group.relations.First().id, group.relations.Last().id));
                            }
                            else if (group.dense != null)
                            {
                                indexInfos.Add(new IndexInfo(passedBC, i, 1, group.dense.id[0], group.dense.id.Sum()));
                            }
                        }
                    }
                    pb2 = null;
                });
                waiting.Add(tasked);
            }

            Task.WaitAll(waiting.ToArray());

            //Now, we should save the IndexInfo list to disk and sort it into sub-indexes
            var indexList = indexInfos.ToList();
            SaveIndexInfo(indexList);
            SplitIndexData(indexList);
            SaveBlockInfo();

            sw.Stop();
            Log.WriteLog("File indexed in " + sw.Elapsed);
        }

        private void LoadIndex()
        {
            List<IndexInfo> indexes = new List<IndexInfo>();
            string filename = outputPath + fi.Name + ".indexinfo";
            var data = File.ReadAllLines(filename);
            foreach (var line in data)
            {
                string[] subData2 = line.Split(":");
                indexes.Add(new IndexInfo(subData2[0].ToInt(), subData2[1].ToInt(), (byte)subData2[2].ToInt(), subData2[3].ToLong(), subData2[4].ToLong()));
            }
            SplitIndexData(indexes);
        }

        private void SplitIndexData(List<IndexInfo> indexes)
        {
            nodeIndex = indexes.Where(i => i.groupType == 1).OrderBy(i => i.minId).ToList();
            wayIndex = indexes.Where(i => i.groupType == 2).OrderBy(i => i.minId).ToList();
            relationIndex = indexes.Where(i => i.groupType == 3).OrderBy(i => i.minId).ToList();

            Log.WriteLog("File has " + relationIndex.Count + " relation groups, " + wayIndex.Count + " way groups, and " + nodeIndex.Count + " node groups");
        }

        /// <summary>
        /// If a block is already in memory, load it. If it isn't, load it from disk and add it to memory.
        /// </summary>
        /// <param name="blockId">the ID for the block in question</param>
        /// <returns>the PrimitiveBlock requested</returns>
        private PrimitiveBlock GetBlock(long blockId)
        {
            //Track that this entry was requested for this processing block.
            //If the block is in memory, return it.
            //If not, load it from disk and return it.
            PrimitiveBlock results;
            if (!activeBlocks.TryGetValue(blockId, out results))
            {
                results = GetBlockFromFile(blockId);
                activeBlocks.TryAdd(blockId, results);
                accessedBlocks.TryAdd(blockId, true);
            }

            return results;
        }

        /// <summary>
        /// Loads a PrimitiveBlock from the PBF file.
        /// </summary>
        /// <param name="blockId">the block to read from the file</param>
        /// <returns>the PrimitiveBlock requested</returns>
        /// 
        private PrimitiveBlock GetBlockFromFile(long blockId)
        {
            long pos1 = blockPositions[blockId];
            int size1 = blockSizes[blockId];
            byte[] thisblob1 = new byte[size1];

            using (var fs = File.OpenRead(fi.FullName))
            {
                fs.Seek(pos1, SeekOrigin.Begin);
                fs.Read(thisblob1, 0, size1);
            }

            return DecodeBlock(thisblob1);
        }

        /// <summary>
        /// Converts the byte array for a block into the PrimitiveBlock object.
        /// </summary>
        /// <param name="blockBytes">the bytes making up the block</param>
        /// <returns>the PrimitiveBlock object requested.</returns>
        private static PrimitiveBlock DecodeBlock(byte[] blockBytes)
        {
            var ms2 = new MemoryStream(blockBytes);
            var b2 = Serializer.Deserialize<Blob>(ms2);
            var ms3 = new MemoryStream(b2.zlib_data);
            var dms2 = new ZLibStream(ms3, CompressionMode.Decompress);

            var pulledBlock = Serializer.Deserialize<PrimitiveBlock>(dms2);
            return pulledBlock;
        }

        private static Relation FindRelationInPrimGroup(List<Relation> primRels, long relId)
        {
            int min = 0;
            int max = primRels.Count;
            int current = (int)Math.Round((double)max / 2.0f, MidpointRounding.AwayFromZero);
            int prevCheck = 0;
            while (min != max && prevCheck != current) //This is a B-Tree search on an array
            {
                var check = primRels[current];
                if (check.id < relId) //This max is below our way, shift min up
                {
                    min = current;
                }
                else if (check.id > relId) //this max is over our way, shift max down
                {
                    max = current;
                }
                else
                    return check;

                prevCheck = current;
                current = (min + max) / 2;
            }
            return null;
        }

        TagsCollection GetTags(List<uint> keys, List<uint> vals)
        {
            var tags = new TagsCollection(keys.Count);
            for (int i = 0; i < keys.Count; i++)
                tags.Add(new Tag(currentBlockStringTable[(int)keys[i]], currentBlockStringTable[(int)vals[i]]));

            return tags;
        }

        TagsCollection GetTags(List<byte[]> stringTable, List<uint> keys, List<uint> vals)
        {
            var tags = new TagsCollection(keys.Count);
            for (int i = 0; i < keys.Count; i++)
                tags.Add(new Tag(Encoding.UTF8.GetString(stringTable[(int)keys[i]]), Encoding.UTF8.GetString(stringTable[(int)vals[i]])));

            return tags;
        }

        /// <summary>
        /// Processes the requested relation into an OSMSharp CompleteRelation from the currently opened file
        /// </summary>
        /// <param name="relationId">the relation to load and process</param>
        /// <param name="ignoreUnmatched">if true, skip entries that don't get a TagParser match applied to them.</param>
        /// <returns>an OSMSharp CompleteRelation, or null if entries are missing, the elements were unmatched and ignoreUnmatched is true, or there were errors creating the object.</returns>
        private CompleteRelation MakeCompleteRelation(long relationId, bool ignoreUnmatched = false, PrimitiveBlock relationBlock = null)
        {
            try
            {
                var indexInfo = FindBlockInfoForRelation(relationId);

                if (relationBlock == null)
                    relationBlock = GetBlock(indexInfo.blockId);

                var relPrimGroup = relationBlock.primitivegroup[indexInfo.groupId];
                var rel = FindRelationInPrimGroup(relPrimGroup.relations, relationId);
                var stringData = relationBlock.stringtable.s;
                bool canProcess = false;
                //sanity check - if this relation doesn't have inner or outer role members,
                //its not one i can process.
                foreach (var role in rel.roles_sid)
                {
                    string roleType = currentBlockStringTable[role];
                    if (roleType == "inner" || roleType == "outer")
                    {
                        canProcess = true; //I need at least one outer, and inners require outers.
                        break;
                    }
                }

                if (!canProcess)
                    return null;

                //If I only want elements that show up in the map, and exclude areas I don't currently match,
                //I have to knows my tags BEFORE doing the rest of the processing.
                CompleteRelation r = new CompleteRelation();
                r.Id = relationId;
                r.Tags = GetTags(rel.keys, rel.vals);

                if (ignoreUnmatched)
                {
                    var tpe = TagParser.GetStyleForOsmWay(r.Tags, styleSet);
                    if (tpe.Name == TagParser.defaultStyle.Name)
                        return null; //This is 'unmatched', skip processing this entry.
                }

                int capacity = rel.memids.Count;
                r.Members = new CompleteRelationMember[capacity];
                int hint = -1;

                long idToFind = 0;
                for (int i = 0; i < capacity; i++)
                {
                    idToFind += rel.memids[i]; //memIds is delta-encoded
                    Relation.MemberType typeToFind = rel.types[i];
                    CompleteRelationMember c = new CompleteRelationMember();
                    c.Role = Encoding.UTF8.GetString(stringData[rel.roles_sid[i]]);
                    switch (typeToFind)
                    {
                        case Relation.MemberType.NODE://The FeatureInterpreter doesn't use nodes from a relation. COULD do this to include where to put text or whatnot.
                            break;
                        case Relation.MemberType.WAY:
                            var wayKey = FindBlockInfoForWay(idToFind, out int indexPosition, hint);
                            hint = indexPosition;
                            c.Member = MakeCompleteWay(idToFind, hint, false);
                            break;
                        case Relation.MemberType.RELATION: //ignore meta-relations
                            break;
                    }
                    r.Members[i] = c;
                }

                //Some memory cleanup slightly early, in an attempt to free up RAM faster.
                rel = null;
                return r;
            }
            catch (Exception ex)
            {
                Log.WriteLog("relation failed:" + ex.Message, Log.VerbosityLevels.Errors);
                return null;
            }
        }

        private static Way FindWayInPrimGroup(List<Way> primWays, long wayId)
        {
            int min = 0;
            int max = primWays.Count;
            int current = max / 2;
            int prevCheck = 0;
            while (min != max && prevCheck != current) //This is a B-Tree search on an array
            {
                var check = primWays[current];
                if (check.id < wayId) //This max is below our way, shift min up
                {
                    min = current;
                }
                else if (check.id > wayId) //this max is over our way, shift max down
                {
                    max = current;
                }
                else
                    return check;

                prevCheck = current;
                current = (min + max) / 2;
            }
            return null;
        }

        /// <summary>
        /// Processes the requested way from the currently open file into an OSMSharp CompleteWay
        /// </summary>
        /// <param name="wayId">the way Id to process</param>
        /// <param name="hints">a list of currently loaded blocks to check before doing a full BTree search for entries</param>
        /// <param name="ignoreUnmatched">if true, returns null if this element's tags only match the default style.</param>
        /// <returns>the CompleteWay object requested, or null if skipUntagged or ignoreUnmatched checks skip this elements, or if there is an error processing the way</returns>
        private CompleteWay MakeCompleteWay(long wayId, int hint = -1, bool ignoreUnmatched = false)
        {
            try
            {
                var wayBlockValues = FindBlockInfoForWay(wayId, out var position, hint);

                PrimitiveBlock wayBlock = GetBlock(wayBlockValues.blockId);
                var wayPrimGroup = wayBlock.primitivegroup[wayBlockValues.groupId];
                var way = FindWayInPrimGroup(wayPrimGroup.ways, wayId);
                if (way == null)
                    return null; //way wasn't in the block it was supposed to be in.

                return MakeCompleteWay(way, ignoreUnmatched);
            }
            catch (Exception ex)
            {
                Log.WriteLog("MakeCompleteWay failed: " + ex.Message + ex.StackTrace, Log.VerbosityLevels.Errors);
                return null; //Failed to get way, probably because a node didn't exist in the file.
            }
        }

        /// <summary>
        /// Processes the requested way from the currently open file into an OSMSharp CompleteWay
        /// </summary>
        /// <param name="way">the way, in PBF form</param>
        /// <param name="ignoreUnmatched">if true, returns null if this element's tags only match the default style.</param>
        /// <returns>the CompleteWay object requested, or null if skipUntagged or ignoreUnmatched checks skip this elements, or if there is an error processing the way</returns>
        private CompleteWay MakeCompleteWay(Way way, bool ignoreUnmatched = false)
        {
            try
            {
                CompleteWay finalway = new CompleteWay();
                finalway.Id = way.id;
                //We always need to apply tags here, so we can either skip after (if IgnoredUmatched is set) or to pass along tag values correctly.
                finalway.Tags = GetTags(way.keys, way.vals);


                if (ignoreUnmatched)
                {
                    if (TagParser.GetStyleForOsmWay(finalway.Tags, styleSet).Name == TagParser.defaultStyle.Name)
                        return null; //don't process this one, we said not to load entries that aren't already in our style list.
                }

                //NOTES:
                //This gets all the entries we want from each node, then loads those all in 1 pass per referenced block.
                //This is significantly faster than doing a GetBlock per node when 1 block has mulitple entries
                //its a little complicated but a solid performance boost.
                long idToFind = 0; //more deltas 
                Dictionary<int, IndexInfo> nodeInfoEntries = new Dictionary<int, IndexInfo>(); //hint(position in array), IndexInfo
                int hint = -1; //last result for previous node.
                Dictionary<long, int> nodesByIndexInfo = new Dictionary<long, int>(); //nodeId, hint(Position in array)

                for (int i = 0; i < way.refs.Count; i++)
                {
                    idToFind += way.refs[i];
                    var blockInfo = FindBlockInfoForNode(idToFind, out var index, hint);
                    hint = index;
                    nodeInfoEntries.TryAdd(index, blockInfo);
                    nodesByIndexInfo.TryAdd(idToFind, hint);
                }
                var nodesByBlockGroup = nodesByIndexInfo.ToLookup(k => k.Value, v => v.Key); //hint(Position in array), nodeIDs

                finalway.Nodes = new OsmSharp.Node[way.refs.Count];
                //Each way is already in its own thread, I think making each of those fire off 1 thread per node block referenced is excessive.
                //and may well hurt performance in most cases due to overhead and locking the concurrentdictionary than it gains.
                Dictionary<long, OsmSharp.Node> AllNodes = new Dictionary<long, OsmSharp.Node>(way.refs.Count);
                foreach (var block in nodesByBlockGroup)
                {
                    var someNodes = GetAllNeededNodesInBlockGroup(nodeInfoEntries[block.Key], block.OrderBy(b => b).ToArray());
                    foreach (var n in someNodes)
                        AllNodes.Add(n.Key, n.Value);
                }

                //Iterate over the list of referenced Nodes again, but this time to assign created nodes to the final results.
                idToFind = 0;
                for (int i = 0; i < way.refs.Count; i++)
                {
                    idToFind += way.refs[i]; //delta coding.
                    finalway.Nodes[i] = AllNodes[idToFind];
                }

                return finalway;
            }
            catch (Exception ex)
            {
                Log.WriteLog("MakeCompleteWay failed: " + ex.Message + ex.StackTrace, Log.VerbosityLevels.Errors);
                return null; //Failed to get way, probably because a node didn't exist in the file.
            }
        }

        /// <summary>
        /// Returns the Nodes that have tags applied from a block.
        /// </summary>
        /// <param name="block">the block of Nodes to search through</param>
        /// <param name="ignoreUnmatched">if true, skip nodes that have tags that only match the default TaParser style.</param>
        /// <returns>a list of Nodes with tags, which may have a length of 0.</returns>
        private ConcurrentBag<OsmSharp.Node> GetTaggedNodesFromBlock(PrimitiveBlock block, bool ignoreUnmatched = false)
        {
            ConcurrentBag<OsmSharp.Node> taggedNodes = new ConcurrentBag<OsmSharp.Node>();
            Parallel.For(0, block.primitivegroup.Count, (b) =>
            {
                nodesProcessed++;
                var dense = block.primitivegroup[b].dense;

                //Shortcut: if dense.keys.count == dense.id.count, there's no tagged nodes at all here (0 means 'no keys', and all 0's means every entry has no keys)
                if (dense.keys_vals.Count == dense.id.Count)
                    return;

                var granularity = block.granularity;
                var lat_offset = block.lat_offset;
                var lon_offset = block.lon_offset;
                var stringData = block.stringtable.s.Select(st => Encoding.UTF8.GetString(st)).ToArray();

                //sort out tags ahead of time.
                int entryCounter = 0;
                List<Tuple<int, string, string>> idKeyVal = new List<Tuple<int, string, string>>((dense.keys_vals.Count - dense.id.Count) / 2); //This could be a Dict<id, tags>
                for (int i = 0; i < dense.keys_vals.Count; i++)
                {
                    if (dense.keys_vals[i] == 0)
                    {
                        //skip to next entry.
                        entryCounter++;
                        continue;
                    }

                    idKeyVal.Add(
                        Tuple.Create(entryCounter,
                        stringData[dense.keys_vals[i++]], //i++ returns i, but increments the value.
                        stringData[dense.keys_vals[i]]
                    ));
                }
                var decodedTags = idKeyVal.ToLookup(k => k.Item1, v => new OsmSharp.Tags.Tag(v.Item2, v.Item3));
                var lastTaggedNode = idKeyVal.Last().Item1;

                var index = -1;
                long nodeId = 0;
                long lat = 0;
                long lon = 0;
                foreach (var denseNode in dense.id)
                {
                    index++;
                    nodeId += denseNode;
                    lat += dense.lat[index];
                    lon += dense.lon[index];

                    if (!decodedTags[index].Any())
                        continue;

                    //now, start loading keys/values
                    OsmSharp.Tags.TagsCollection tc = new OsmSharp.Tags.TagsCollection(decodedTags[index]);

                    if (ignoreUnmatched)
                    {
                        if (TagParser.GetStyleForOsmWay(tc, styleSet) == TagParser.defaultStyle)
                            continue;
                    }

                    OsmSharp.Node n = new OsmSharp.Node();
                    n.Id = nodeId;
                    n.Latitude = DecodeLatLon(lat, lat_offset, granularity);
                    n.Longitude = DecodeLatLon(lon, lon_offset, granularity);
                    n.Tags = tc;

                    //if bounds checking, drop nodes that aren't needed.
                    if (bounds == null || (n.Latitude >= bounds.MinY && n.Latitude <= bounds.MaxY && n.Longitude >= bounds.MinX && n.Longitude <= bounds.MaxX))
                        taggedNodes.Add(n);

                    if (index >= lastTaggedNode)
                        break;
                }
            });

            return taggedNodes;
        }

        /// <summary>
        /// Pulls out all requested nodes from a block. Significantly faster to pull all nodes per block this way than to run through the list for each node.
        /// </summary>
        /// <param name="blockId">the block to pull nodes out of</param>
        /// <param name="nodeIds">the IDs of nodes to load from this block</param>
        /// <returns>a Dictionary of the node ID and corresponding values.</returns>
        private Dictionary<long, OsmSharp.Node> GetAllNeededNodesInBlockGroup(IndexInfo blockAndGroup, long[] nodeIds)
        {
            Dictionary<long, OsmSharp.Node> results = new Dictionary<long, OsmSharp.Node>(nodeIds.Length);
            int arrayIndex = 0;

            var block = GetBlock(blockAndGroup.blockId);
            var group = block.primitivegroup[blockAndGroup.groupId].dense;
            var granularity = block.granularity;
            var lat_offset = block.lat_offset;
            var lon_offset = block.lon_offset;

            int index = -1;
            long nodeCounter = 0;
            long latDelta = 0;
            long lonDelta = 0;
            var denseIds = group.id;
            var dLat = group.lat;
            var dLon = group.lon;
            var nodeToFind = nodeIds[arrayIndex];
            while (index < denseIds.Count)
            {
                index++;

                nodeCounter += denseIds[index];
                latDelta += dLat[index];
                lonDelta += dLon[index];

                if (nodeToFind == nodeCounter)
                {
                    OsmSharp.Node filled = new OsmSharp.Node();
                    filled.Id = nodeCounter;
                    filled.Latitude = DecodeLatLon(latDelta, lat_offset, granularity);
                    filled.Longitude = DecodeLatLon(lonDelta, lon_offset, granularity);
                    results.Add(nodeCounter, filled);
                    arrayIndex++;
                    if (arrayIndex == nodeIds.Length)
                        return results;
                    nodeToFind = nodeIds[arrayIndex];
                }
            }
            return results;
        }

        /// <summary>
        /// Determine which node in the file has the given Node, using a BTree search on the index.
        /// </summary>
        /// <param name="nodeId">The node to find in the currently opened file</param>
        /// <param name="hints">a list of blocks to check first, assuming that nodes previously searched are likely to be near each other. Ignored if more than 20 entries are in the list. </param>
        /// <returns>the block ID containing the requested node</returns>
        /// <exception cref="Exception">Throws an exception if the nodeId isn't found in the current file.</exception>
        private IndexInfo FindBlockInfoForNode(long nodeId, out int current, int hint = -1) //BTree
        {
            //This is the most-called function in this class, and therefore the most performance-dependent.

            //Hints is a list of blocks we're already found in the relevant way. Odds are high that
            //any node I need to find is in the same block as another node I've found.
            //This should save a lot of time searching the list when I have already found some blocks
            //and shoudn't waste too much time if it isn't in a block already found.
            //foreach (var h in hints)


            //ways will hit a couple thousand blocks, nodes hit hundred of thousands of blocks.
            //This might help performance on ways, but will be much more noticeable on nodes.
            int min = 0;
            int max = nodeIndexEntries;
            if (hint == -1)
                current = startNodeBtreeIndex;
            else
                current = hint;
            int lastCurrent = current;
            while (min != max)
            {
                var check = nodeIndex[current];
                if ((check.minId <= nodeId) && (nodeId <= check.maxId))
                    return check;

                else if (check.minId > nodeId) //this ways minimum is larger than our way, shift maxs down
                    max = current;
                else if (check.maxId < nodeId) //this ways maximum is smaller than our way, shift min up.
                    min = current;

                lastCurrent = current;
                current = (min + max) / 2;
                if (lastCurrent == current)
                {
                    //We have an issue, and are gonna infinite loop. Fix it.
                    //Check if we're in the gap between blocks.
                    var checkUnder = wayIndex[current - 1];
                    var checkOver = wayIndex[current + 1];

                    if (checkUnder.maxId < nodeId && checkOver.minId > nodeId)
                        //exception, we're between blocks.
                        throw new Exception("Node Not Found");

                    //We are probably in a weird edge case where min and max are 1 or 2 apart and I just need nudged over 1 spot.
                    if (nodeId < checkUnder.maxId)
                        current--;
                    else if (nodeId > checkOver.minId)
                        current++;
                    else
                        min = max;
                }
            }

            throw new Exception("Node Not Found");
        }

        /// <summary>
        /// Determine which node in the file has the given Way, using a BTree search on the index.
        /// </summary>
        /// <param name="wayId">The way to find in the currently opened file</param>
        /// <param name="hints">a list of blocks to check first, assuming that blocks previously searched are likely to be near each other. Ignored if more than 20 entries are in the list. </param>
        /// <returns>the block ID containing the requested way</returns>
        /// <exception cref="Exception">Throws an exception if the way isn't found in the current file.</exception>

        //This is a modified BTree search. We start at the position of the LAST found entry (hint)
        //If the hint is correct, we didn't need to do any additional searching.
        //If the hint is not correct, we continue the BTree search with up to 1 additional search than we needed if we started at the center.
        private IndexInfo FindBlockInfoForWay(long wayId, out int current, int hint = -1)
        {
            int min = 0;
            int max = wayIndexEntries;
            if (hint != -1)
                current = hint;
            else
                current = startWayBtreeIndex;
            int lastCurrent = current;
            while (min != max)
            {
                var check = wayIndex[current];
                if ((check.minId <= wayId) && (wayId <= check.maxId))
                    return check;

                else if (check.minId > wayId) //this ways minimum is larger than our way, shift maxs down
                    max = current;
                else if (check.maxId < wayId) //this ways maximum is smaller than our way, shift min up.
                    min = current;

                lastCurrent = current;
                current = (min + max) / 2;
                if (lastCurrent == current)
                {
                    //We have an issue, and are gonna infinite loop. Fix it.
                    //Check if we're in the gap between blocks.
                    var checkUnder = wayIndex[current - 1];
                    var checkOver = wayIndex[current + 1];

                    if (checkUnder.maxId < wayId && checkOver.minId > wayId)
                        //exception, we're between blocks.
                        throw new Exception("Way Not Found");

                    //We are probably in a weird edge case where min and max are 1 or 2 apart and I just need nudged over 1 spot.
                    if (wayId < checkUnder.maxId)
                        current--;
                    else if (wayId > checkOver.minId)
                        current++;
                    else
                        min = max;
                }
            }

            throw new Exception("Way Not Found");
        }

        private IndexInfo FindBlockInfoForRelation(long relationId) //BTree 
        {
            int min = 0;
            int max = relationIndexEntries;
            int current = startRelationBtreeIndex;

            int lastCurrent = current;
            while (min != max)
            {
                var check = relationIndex[current];
                if ((check.minId <= relationId) && (relationId <= check.maxId))
                    return check;

                else if (check.minId > relationId) //this ways minimum is larger than our way, shift maxs down
                    max = current;
                else if (check.maxId < relationId) //this ways maximum is smaller than our way, shift min up.
                    min = current;

                lastCurrent = current;
                current = (min + max) / 2;
                if (lastCurrent == current)
                {
                    //We have an issue, and are gonna infinite loop. Fix it.
                    //Check if we're in the gap between blocks.
                    var checkUnder = relationIndex[current - 1];
                    var checkOver = relationIndex[current + 1];

                    if (checkUnder.maxId < relationId && checkOver.minId > relationId)
                        //exception, relation ID legit not present in the data.
                        throw new Exception("Relation Not Found");

                    //We are probably in a weird edge case where min and max are 1 or 2 apart and I just need nudged over 1 spot.
                    if (relationId < checkUnder.maxId)
                        current--;
                    else if (relationId > checkOver.minId)
                        current++;
                    else
                        min = max;
                }
            }

            throw new Exception("Relation Not Found");
        }

        public ConcurrentBag<ICompleteOsmGeo> GetGeometryFromGroup(int blockId, PrimitiveGroup primgroup, bool onlyTagMatchedEntries = false)
        {
            //This grabs the chosen block, populates everything in it to an OsmSharp.Complete object and returns that list
            ConcurrentBag<ICompleteOsmGeo> results = new ConcurrentBag<ICompleteOsmGeo>();
            try
            {
                //Attempting to clear up some memory slightly faster, but this should be redundant.
                relList.Clear();
                var block = GetBlock(blockId);
                currentBlockStringTable = block.stringtable.s.Select(st => Encoding.UTF8.GetString(st)).ToArray();
                if (primgroup.relations != null && primgroup.relations.Count > 0)
                {
                    //Some relation blocks can hit 22GB of RAM on their own. Low-resource machines will fail, and should roll into the LastChance path automatically.
                    foreach (var r in primgroup.relations)
                        relList.Add(Task.Run(() => { results.Add(MakeCompleteRelation(r.id, onlyTagMatchedEntries, block)); totalProcessEntries++; }));
                }
                else if (primgroup.ways != null && primgroup.ways.Count > 0)
                {
                    foreach (var r in primgroup.ways)
                    {
                        relList.Add(Task.Run(() => { results.Add(MakeCompleteWay(r, onlyTagMatchedEntries)); totalProcessEntries++; }));
                    }
                }
                else
                {
                    //Useful node lists are so small, they lose performance from splitting each step into 1 task per entry.
                    //Inline all that here as one task and return null to skip the rest.
                    relList.Add(Task.Run(() =>
                    {
                        try
                        {
                            var nodes = GetTaggedNodesFromBlock(block, onlyTagMatchedEntries);
                            totalProcessEntries += nodes.Count;
                            results = new ConcurrentBag<ICompleteOsmGeo>(nodes);
                        }
                        catch (Exception ex)
                        {
                            Log.WriteLog("Processing node failed: " + ex.Message, Log.VerbosityLevels.Errors);
                        }
                    }));
                }

                Task.WaitAll(relList.ToArray());

                //Moved this logic here to free up RAM by removing blocks once we're done reading data from the hard drive. Should result in fewer errors at the ProcessReaderResults step.
                //Slightly more complex: only remove blocks we didn't access last call. saves some serialization effort. Small RAM trade for 30% speed increase.
                if (!keepAllBlocksInRam)
                    foreach (var blockRead in activeBlocks)
                    {
                        if (!accessedBlocks.ContainsKey(blockRead.Key))
                            activeBlocks.TryRemove(blockRead.Key, out var xx);
                    }
                accessedBlocks.Clear();
                return results;
            }
            catch (Exception ex)
            {
                Log.WriteLog("Error getting geometry: " + ex.Message, Log.VerbosityLevels.Errors);
                throw ex; //In order to reprocess this block in last-chance mode.
                          //return null;
            }
        }

        //Taken from OsmSharp (MIT License)
        /// <summary>
        /// Turns a PBF's dense stored data into a standard latitude or longitude value in degrees.
        /// </summary>
        /// <param name="valueOffset">the valueOffset for the given node</param>
        /// <param name="blockOffset">the offset for the block currently loaded</param>
        /// <param name="blockGranularity">the granularity value of the block data is loaded from</param>
        /// <returns>a double represeting the lat or lon value for the given dense values</returns>
        private static double DecodeLatLon(long valueOffset, long blockOffset, long blockGranularity)
        {
            return .000000001 * (blockOffset + (blockGranularity * valueOffset));
        }
        //end OsmSharp copied functions.

        private async void SaveIndexInfo(List<IndexInfo> index)
        {
            string filename = outputPath + fi.Name + ".indexinfo";
            string[] data = new string[index.Count];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = index[i].blockId + ":" + index[i].groupId + ":" + index[i].groupType + ":" + index[i].minId + ":" + index[i].maxId;
            }
            File.WriteAllLines(filename, data);
        }

        /// <summary>
        /// Saves the block positions created for the currently opened file to their own files, so that they can be read instead of created if processing needs to resume later.
        /// </summary>
        private async void SaveBlockInfo()
        {
            string filename = outputPath + fi.Name + ".blockinfo";
            //now deserialize
            string[] data = new string[blockPositions.Count];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = i + ":" + blockPositions[i] + ":" + blockSizes[i];
            }
            File.WriteAllLines(filename, data);
        }

        /// <summary>
        /// Loads indexed data from a previous run from file, to skip reprocessing the entire file.
        /// </summary>
        private void LoadBlockInfo()
        {
            try
            {
                string filename = outputPath + fi.Name + ".blockinfo";
                string[] data = File.ReadAllLines(filename);
                blockPositions = new Dictionary<long, long>(data.Length);
                blockSizes = new Dictionary<long, int>(data.Length);

                for (int i = 0; i < data.Length; i++)
                {
                    string[] subdata = data[i].Split(":");
                    blockPositions[i] = long.Parse(subdata[1]);
                    blockSizes[i] = int.Parse(subdata[2]);
                }

                LoadIndex();
                SetOptimizationValues();
            }
            catch (Exception ex)
            {
                return;
            }
        }

        /// <summary>
        /// Use the indexed data to store a few values needed for optimiazations to work at their best.
        /// </summary>
        private void SetOptimizationValues()
        {
            nodeIndexEntries = nodeIndex.Count;
            wayIndexEntries = wayIndex.Count;
            relationIndexEntries = relationIndex.Count;

            startNodeBtreeIndex = (int)Math.Round((double)nodeIndex.Count / 2.0f, MidpointRounding.AwayFromZero);
            startWayBtreeIndex = (int)Math.Round((double)wayIndex.Count / 2.0f, MidpointRounding.AwayFromZero);
            startRelationBtreeIndex = (int)Math.Round((double)relationIndex.Count / 2.0f, MidpointRounding.AwayFromZero);

            firstWayBlock = wayIndex.Min(w => w.blockId);
        }

        /// <summary>
        /// Saves the currently completed block to a file, so we can resume without reprocessing existing data if needed.
        /// </summary>
        /// <param name="blockID">the block most recently processed</param>
        private void SaveCurrentBlockAndGroup(long blockID, int groupId)
        {
            string filename = outputPath + fi.Name + ".progress";
            File.WriteAllTextAsync(filename, blockID.ToString() + ":" + groupId);
        }

        //Loads the most recently completed block from a file to resume without doing duplicate work.
        private int FindLastCompletedBlock()
        {
            try
            {
                string filename = outputPath + fi.Name + ".progress";
                int blockID = int.Parse(File.ReadAllText(filename).Split(":")[0]);
                return blockID;
            }
            catch (Exception ex)
            {
                return -1;
            }
        }

        private int FindLastCompletedGroup()
        {
            try
            {
                string filename = outputPath + fi.Name + ".progress";
                int groupID = int.Parse(File.ReadAllText(filename).Split(":")[1]);
                return groupID;
            }
            catch (Exception ex)
            {
                return -1;
            }
        }

        /// <summary>
        /// Delete indexes and progress file.
        /// </summary>
        private void CleanupFiles()
        {
            try
            {
                if (!keepIndexFiles)
                {
                    foreach (var file in Directory.EnumerateFiles(outputPath, "*.blockinfo"))
                        File.Delete(file);

                    foreach (var file in Directory.EnumerateFiles(outputPath, "*.indexInfo"))
                        File.Delete(file);
                }

                foreach (var file in Directory.EnumerateFiles(outputPath, "*.progress"))
                    File.Delete(file);
            }
            catch (Exception ex)
            {
                Log.WriteLog("Error cleaning up files: " + ex.Message, Log.VerbosityLevels.Errors);
            }
        }

        /// <summary>
        /// Called to display periodic performance summaries on the console while a file is being processed.
        /// </summary>
        public void ShowWaitInfo()
        {
            Task.Run(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    Log.WriteLog("Current stats:");
                    Log.WriteLog("Groups completed this run: " + (timeListRelations.Count + timeListWays.Count + nodesProcessed));
                    Log.WriteLog("Processing tasks: " + relList.Count(r => !r.IsCompleted));
                    Log.WriteLog("Total Processed Entries: " + totalProcessEntries);
                    if (!timeListRelations.IsEmpty || !timeListWays.IsEmpty)
                    {
                        var relGroups = relationIndex.Count;
                        var wayGroups = wayIndex.Count;

                        double relationTimeLeft = 0;
                        double wayTimeLeft = 0;
                        if (timeListRelations.Count > 0 && timeListWays.Count == 0)
                            relationTimeLeft = timeListRelations.Average(t => t.TotalSeconds) * (relGroups - timeListRelations.Count);
                        if (timeListWays.Count > 0)
                            wayTimeLeft = timeListWays.Average(t => t.TotalSeconds) * (wayGroups - timeListWays.Count);

                        if (relationTimeLeft > 0)
                            Log.WriteLog("Time to complete Relation groups: " + new TimeSpan((long)relationTimeLeft * 10000000));
                        else if (wayTimeLeft > 0)
                            Log.WriteLog("Time to complete Way groups: " + new TimeSpan((long)wayTimeLeft * 10000000));
                    }
                    Thread.Sleep(60000);
                }
            }, token);
        }

        /// <summary>
        /// Take a list of OSMSharp CompleteGeo items, and convert them into PraxisMapper's Place objects.
        /// </summary>
        /// <param name="items">the OSMSharp CompleteGeo items to convert</param>
        /// <param name="saveFilename">The filename to save data to. Ignored if saveToDB is true</param>
        /// <param name="saveToDb">If true, insert the items directly to the database instead of exporting to files.</param>
        /// <param name="onlyTagMatchedElements">if true, only loads in elements that dont' match the default entry for a TagParser style set</param>
        /// <returns>the Task handling the conversion process</returns>
        public void ProcessReaderResults(IEnumerable<ICompleteOsmGeo> items, long blockId, int groupId)
        {
            //This one is easy, we just dump the geodata to the file.
            string saveFilename = outputPath + Path.GetFileNameWithoutExtension(fi.Name) + "-" + blockId + "-" + groupId;
            ConcurrentBag<DbTables.Place> elements = new ConcurrentBag<DbTables.Place>();

            if (items == null || !items.Any())
                return;

            relList = new ConcurrentBag<Task>();
            foreach (var r in items)
            {
                if (r != null)
                    relList.Add(Task.Run(() => { var e = GeometrySupport.ConvertOsmEntryToPlace(r); if (e != null) elements.Add(e); }));
            }
            Task.WaitAll(relList.ToArray());

            if (boundsEntry != null)
                elements = new ConcurrentBag<DbTables.Place>(elements.Where(e => boundsEntry.Intersects(e.ElementGeometry)));

            if (elements.IsEmpty)
                return;

            //Single check per block to fix points having 0 size.
            if (elements.First().SourceItemType == 1)
                foreach (var e in elements)
                    e.AreaSize = ConstantValues.resolutionCell10;

            if (processingMode == "center")
                foreach (var e in elements)
                    e.ElementGeometry = e.ElementGeometry.Centroid;

            if (saveToDB) //If this is on, we skip the file-writing part and send this data directly to the DB. Single threaded, but doesn't waste disk space with intermediate files.
            {
                var db = new PraxisContext();
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                db.Places.AddRange(elements);
                db.SaveChanges();
                return;
            }
            else if (splitByStyleSet)
            {
                saveFilename = outputPath + Path.GetFileNameWithoutExtension(fi.Name) + "-";

                Dictionary<string, StringBuilder> geomDataByMatch = new Dictionary<string, StringBuilder>();
                Dictionary<string, StringBuilder> tagDataByMatch = new Dictionary<string, StringBuilder>();
                foreach (var s in TagParser.allStyleGroups[styleSet])
                {
                    geomDataByMatch.Add(s.Value.Name, new StringBuilder());
                    tagDataByMatch.Add(s.Value.Name, new StringBuilder());
                }
                //Save to separate files based on which style set entry each one matched up to.
                foreach (var md in elements)
                {
                    var areatype = TagParser.GetAreaType(md.Tags, styleSet);
                    var name = TagParser.GetPlaceName(md.Tags);
                    geomDataByMatch[areatype].Append(md.SourceItemID).Append('\t').Append(md.SourceItemType).Append('\t').Append(md.ElementGeometry.AsText()).Append('\t').Append(md.AreaSize).Append('\t').Append(Guid.NewGuid()).Append("\r\n");
                    foreach (var t in md.Tags)
                        tagDataByMatch[areatype].Append(md.SourceItemID).Append('\t').Append(md.SourceItemType).Append('\t').Append(t.Key).Append('\t').Append(t.Value.Replace("\r", "").Replace("\n", "")).Append("\r\n"); //Might also need to sanitize / and ' ?
                }

                lock (nodeLock) //This only needs a lock for nodes because that's the only part that does writes in parallel, but the lock cost is trivial when its not used by nodes.
                {
                    var writeTasks = new List<Task>(geomDataByMatch.Count * 2);
                    foreach (var dataSet in geomDataByMatch)
                    {
                        if (dataSet.Value.Length > 0)
                        {
                            writeTasks.Add(Task.Run(() => File.AppendAllText(saveFilename + dataSet.Key + ".geomData", dataSet.Value.ToString())));
                            writeTasks.Add(Task.Run(() => File.AppendAllText(saveFilename + dataSet.Key + ".tagsData", tagDataByMatch[dataSet.Key].ToString())));
                        }
                    }
                    Task.WaitAll(writeTasks.ToArray());
                }
            }
            else
            {
                //Starts with some data allocated in each 2 stringBuilders to minimize reallocations. In my test setup, 10kb is the median value for all files, and 100kb is enough for 90% of blocks
                StringBuilder geometryBuilds = new StringBuilder(100000); //100kb
                StringBuilder tagBuilds = new StringBuilder(40000); //40kb, tags are usually smaller than geometry.
                foreach (var md in elements)
                {
                    geometryBuilds.Append(md.SourceItemID).Append('\t').Append(md.SourceItemType).Append('\t').Append(md.ElementGeometry.AsText()).Append('\t').Append(md.AreaSize).Append('\t').Append(Guid.NewGuid()).Append("\r\n");
                    foreach (var t in md.Tags)
                        tagBuilds.Append(md.SourceItemID).Append('\t').Append(md.SourceItemType).Append('\t').Append(t.Key).Append('\t').Append(t.Value.Replace("\r", "").Replace("\n", "")).Append("\r\n"); //Might also need to sanitize / and ' ?
                }
                try
                {
                    {
                        Parallel.Invoke(
                            () => File.AppendAllText(saveFilename + ".geomData", geometryBuilds.ToString()),
                            () => File.AppendAllText(saveFilename + ".tagsData", tagBuilds.ToString())
                        );
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteLog("Error writing data to disk:" + ex.Message, Log.VerbosityLevels.Errors);
                }
            }

            return; //some invalid options were passed and we didnt run through anything.
        }

        /// <summary>
        /// Pull a single relation out of the given PBF file as an OSMSharp CompleteRelation. Will index the file as normal if needed, but does not clean up the indexed file to allow for reuse later.
        /// </summary>
        /// <param name="filename">The filename containing the relation</param>
        /// <param name="relationId">the relation to process</param>
        /// <returns>The CompleteRelation requested, or null if it was unable to be created from the file.</returns>
        public CompleteRelation LoadOneRelationFromFile(string filename, long relationId)
        {
            Log.WriteLog("Starting to load one relation from file.");
            try
            {
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                PrepareFile(filename);

                var relation = MakeCompleteRelation(relationId);
                Close();
                sw.Stop();
                Log.WriteLog("Processing completed at " + DateTime.Now + ", session lasted " + sw.Elapsed);
                return relation;
            }
            catch (Exception ex)
            {
                while (ex.InnerException != null)
                    ex = ex.InnerException;
                Log.WriteLog("Error processing file: " + ex.Message + ex.StackTrace);
                return null;
            }
        }

        /// <summary>
        /// Pull a single Way out of the given PBF file as an OSMSharp CompleteWay. Will index the file as normal if needed, but does not clean up the indexed file to allow for reuse later.
        /// </summary>
        /// <param name="filename">The filename containing the relation</param>
        /// <param name="wayId">the relation to process</param>
        /// <returns>the CompleteWay requested, or null if it was unable to be created from the file.</returns>
        public CompleteWay LoadOneWayFromFile(string filename, long wayId)
        {
            Log.WriteLog("Starting to load one relation from file.");
            try
            {
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                PrepareFile(filename);

                var way = MakeCompleteWay(wayId, ignoreUnmatched: false);
                Close();
                sw.Stop();
                Log.WriteLog("Processing completed at " + DateTime.Now + ", session lasted " + sw.Elapsed);
                return way;
            }
            catch (Exception ex)
            {
                while (ex.InnerException != null)
                    ex = ex.InnerException;
                Log.WriteLog("Error processing file: " + ex.Message + ex.StackTrace);
                return null;
            }
        }

        public void PrepareFile(string filename)
        {
            Open(filename);
            LoadBlockInfo();
            nextBlockId = 0;
            if (relationIndex.Count == 0)
            {
                IndexFile();
                SetOptimizationValues();
                SaveBlockInfo();
                SaveCurrentBlockAndGroup(BlockCount(), 0);
                nextBlockId = BlockCount() - 1;
            }
            else
            {
                var lastBlock = FindLastCompletedBlock();
                if (lastBlock == -1)
                {
                    nextBlockId = BlockCount() - 1;
                    SaveCurrentBlockAndGroup(BlockCount(), -1);
                }
                else
                    nextBlockId = lastBlock - 1;
            }

            if (displayStatus)
                ShowWaitInfo();
            CheckForThrashing();
        }

        public void CheckForThrashing()
        {
            Task.Run(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    Process proc = Process.GetCurrentProcess();
                    var data = GC.GetGCMemoryInfo();
                    long currentRAM = proc.PrivateMemorySize64;
                    long systemRAM = data.TotalAvailableMemoryBytes;

                    if (currentRAM > (systemRAM * .8))
                    {
                        Log.WriteLog("Force-dumping half of block cache to minimize swap-file thrashing");
                        foreach (var block in activeBlocks)
                            if (Random.Shared.Next() % 2 == 0)
                                activeBlocks.TryRemove(block.Key, out var ignore);

                        GC.Collect();
                    }
                    Thread.Sleep(30000);
                }
            }, token);
        }
    }
}