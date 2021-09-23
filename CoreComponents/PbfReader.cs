using ProtoBuf;
using Ionic.Zlib;
using System.Collections.Concurrent;
using static PraxisCore.DbTables;
using PraxisCore.Support;
using System.Text.Json;
using OsmSharp.Complete;
using System.Text;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using NetTopologySuite.Geometries;
using NetTopologySuite.Geometries.Prepared;
using System.IO;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;

namespace PraxisCore.PbfReader
{
    /// <summary>
    /// PraxisMapper's customized, multithreaded PBF parser. Saves on RAM usage by relying on disk access when needed. Can resume a previous session if stopped for some reason.
    /// </summary>
    public class PbfReader
    {
        //The 5th generation of logic for pulling geometry out of a pbf file. This one is written specfically for PraxisMapper, and
        //doesn't depend on OsmSharp for reading the raw data now. OsmSharp's still used for object types now that there's our own
        //FeatureInterpreter instead of theirs. 

        //TODO:
        //Make some function paramters settings in here, like saveInFile or saveToDb.

        static int initialCapacity = 7993; //ConcurrentDictionary says initial capacity shouldn't be divisible by a small prime number, so i picked the prime closes to 8,000 for initial capacity
        static int initialConcurrency = Environment.ProcessorCount;

        public bool saveToInfile = false;
        public bool saveToDB = false;
        public bool saveToJson = true; //Defaults to the common intermediate output.
        //public bool onlyTaggedAreas = false; //This is somewhat redundant, since all ways/relations will be tagged and storing untagged nodes isnt necessary in PM.
        public bool onlyMatchedAreas = false;

        public string outputPath = "";
        public string filenameHeader = "";

        Task waitInfoTask;

        //Primary function:
        //ProcessFile(filename) should do everything automatically and allow resuming if you stop the app.

        FileInfo fi;
        FileStream fs; // The input file. Output files are used with StreamWriters.

        //<osmId, blockId>
        ConcurrentDictionary<long, long> relationFinder = new ConcurrentDictionary<long, long>(initialConcurrency, initialCapacity);

        //this is blockId, <minNode, maxNode>.
        ConcurrentDictionary<long, Tuple<long, long>> nodeFinder2 = new ConcurrentDictionary<long, Tuple<long, long>>(initialConcurrency, initialCapacity);
        //blockId, minNode, maxNode.
        List<Tuple<long, long, long>> nodeFinderList = new List<Tuple<long, long, long>>(initialCapacity);
        //<blockId, maxWayId> since ways are sorted in order.
        ConcurrentDictionary<long, long> wayFinder = new ConcurrentDictionary<long, long>(initialConcurrency, initialCapacity);// Concurrent needed because loading is threaded.
        List<Tuple<long, long>> wayFinderList = new List<Tuple<long, long>>(initialCapacity);
        int nodeFinderTotal = 0;
        int wayFinderTotal = 0;

        Dictionary<long, long> blockPositions = new Dictionary<long, long>(initialCapacity);
        Dictionary<long, int> blockSizes = new Dictionary<long, int>(initialCapacity);

        Envelope bounds = null; //If not null, reject elements not within it
        IPreparedGeometry boundsEntry = null; //use for precise detection of what to include.

        ConcurrentDictionary<long, PrimitiveBlock> activeBlocks = new ConcurrentDictionary<long, PrimitiveBlock>(8, initialCapacity);
        ConcurrentDictionary<long, bool> accessedBlocks = new ConcurrentDictionary<long, bool>(8, initialCapacity);

        private PrimitiveBlock _block = new PrimitiveBlock();
        private BlobHeader _header = new BlobHeader();

        object msLock = new object(); //reading blocks from disk.
        object fileLock = new object(); //Writing to json file
        object geomFileLock = new object(); //Writing to mariadb LOAD DATA INFILE for StoredOsmElement
        object tagsFileLock = new object(); //Writing to mariadb LOAD DATA INFILE for ElementTags

        long nextBlockId = 0;
        long firstWayBlock = 0;
        long firstRelationBlock = 0;
        int startNodeBtreeIndex = 0;
        int startWayBtreeIndex = 0;
        int wayHintsMax = 12; //Ignore hints if it would be slower checking all of them than just doing a BTree search on 2^12 (4096) blocks
        int nodeHintsMax = 12;

        ConcurrentBag<Task> writeTasks = new ConcurrentBag<Task>(); //Writing to json file, and long-running relation processing
        ConcurrentBag<Task> relList = new ConcurrentBag<Task>(); //Individual, smaller tasks.
        ConcurrentBag<TimeSpan> timeList = new ConcurrentBag<TimeSpan>(); //how long each block took to process.

        //for reference. These are likely to be lost if the application dies partway through processing, since these sit outside the general block-by-block plan.
        //private HashSet<long> knownSlowRelations = new HashSet<long>() {
        //    9488835, //Labrador Sea. 25,000 ways. Stack Overflows on converting to CompleteRelation through defaultFeatureInterpreter.
        //    1205151, //Lake Huron, 14,000 ways. Can Stack overflow joining rings.
        //    148838, //United States. 1029 members but a very large geographic area
        //    9428957, //Gulf of St. Lawrence. 11,000 ways. Can finish processing, so it's somewhere between 11k and 14k that the stack overflow hits.
        //    4039900, //Lake Erie is 1100 ways, originally took ~56 seconds start to finish, now runs in 3-6 seconds on its own.
        //};

        public bool displayStatus = true;

        StreamWriter geomFileStream;
        StreamWriter tagsFileStream;
        StreamWriter jsonFileStream;

        CancellationTokenSource tokensource = new CancellationTokenSource();
        CancellationToken token;

        public PbfReader()
        {
            token = tokensource.Token;
        }

        /// <summary>
        /// Returns how many blocks are in the current PBF file.
        /// </summary>
        /// <returns>long of blocks in the opened file</returns>
        public long BlockCount()
        {
            return blockPositions.Count();
        }

        /// <summary>
        /// Opens up a file for reading. 
        /// </summary>
        /// <param name="filename">the path to the file to read.</param>
        private void Open(string filename)
        {
            fi = new FileInfo(filename);
            fs = File.OpenRead(filename);

            Serializer.PrepareSerializer<PrimitiveBlock>();
            Serializer.PrepareSerializer<Blob>();
        }

        /// <summary>
        /// Closes the currently open file
        /// </summary>
        private void Close()
        {
            fs.Close();
            fs.Dispose();
            EndWaitInfoTask();
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
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();

                Open(filename);
                LoadBlockInfo();
                nextBlockId = 0;
                if (relationFinder.Count == 0)
                {
                    IndexFile();
                    SaveBlockInfo();
                    nextBlockId = BlockCount() - 1;
                    SaveCurrentBlock(BlockCount());
                }
                else
                {
                    nextBlockId = FindLastCompletedBlock() - 1;
                }

                if (displayStatus)
                    ShowWaitInfo();

                if (relationId != 0)
                {
                    filenameHeader += relationId.ToString() + "-";
                    //Get the source relation first
                    var relation = GetRelation(relationId);
                    var NTSrelation = GeometrySupport.ConvertOsmEntryToStoredElement(relation);
                    bounds = NTSrelation.elementGeometry.EnvelopeInternal;
                    var pgf = new PreparedGeometryFactory();
                    boundsEntry = pgf.Create(NTSrelation.elementGeometry);
                }

                if (!saveToDB)
                {
                    if (saveToInfile)
                    {
                        geomFileStream = new StreamWriter(new FileStream(outputPath + filenameHeader + System.IO.Path.GetFileNameWithoutExtension(filename) + ".geomInfile", FileMode.OpenOrCreate));
                        tagsFileStream = new StreamWriter(new FileStream(outputPath + filenameHeader + System.IO.Path.GetFileNameWithoutExtension(filename) + ".tagsInfile", FileMode.OpenOrCreate));
                    }
                    else if (saveToJson)
                        jsonFileStream = new StreamWriter(new FileStream(outputPath + filenameHeader + System.IO.Path.GetFileNameWithoutExtension(filename) + ".json", FileMode.OpenOrCreate));
                }

                for (var block = nextBlockId; block > 0; block--)
                {
                    System.Diagnostics.Stopwatch swBlock = new System.Diagnostics.Stopwatch(); //Includes both GetGeometry and ProcessResults time, but writing to disk is done in a thread independent of this.
                    swBlock.Start();
                    long thisBlockId = block;
                    var geoData = GetGeometryFromBlock(thisBlockId, onlyMatchedAreas, saveToInfile);
                    //There are large relation blocks where you can see how much time is spent writing them or waiting for one entry to
                    //process as the apps drops to a single thread in use, but I can't do much about those if I want to be able to resume a process.
                    if (geoData != null) //This process function is sufficiently parallel that I don't want to throw it off to a Task. The only sequential part is writing the data to the file, and I need that to keep accurate track of which blocks have beeen written to the file.
                    {
                        var wt = ProcessReaderResults(geoData);
                        if (wt != null)
                            writeTasks.Add(wt);
                    }
                    SaveCurrentBlock(block);
                    swBlock.Stop();
                    timeList.Add(swBlock.Elapsed);
                    Log.WriteLog("Block " + block + " processed in " + swBlock.Elapsed);
                }

                Log.WriteLog("Waiting on " + writeTasks.Where(w => !w.IsCompleted).Count() + " additional tasks");
                Task.WaitAll(writeTasks.ToArray());
                Close();
                if (saveToInfile)
                {
                    geomFileStream.Flush();
                    geomFileStream.Close();
                    geomFileStream.Dispose();
                    tagsFileStream.Flush();
                    tagsFileStream.Close();
                    tagsFileStream.Dispose();
                }
                if (saveToJson)
                {
                    jsonFileStream.Flush();
                    jsonFileStream.Close();
                    jsonFileStream.Dispose();
                }
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

        /// <summary>
        /// Given a filename and a relation's ID from OpenStreetMap, processes all elements that intersect the given relation directly to the database.
        /// </summary>
        /// <param name="filename">the PBF file to process</param>
        /// <param name="relationId">the OSM relation ID to use as the basis for loaded data</param>
        /// <returns>the Envelope for the given relation, to be used to identify server bounds.</returns>
        public Envelope GetOneAreaFromFile(string filename, long relationId)
        {
            //Get a bounding box from a single relation, then pull all entries in it from the file to the DB.
            try
            {
                outputPath += relationId.ToString() + "-";
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                Open(filename);
                LoadBlockInfo();
                nextBlockId = 0;
                IndexFile();
                SaveBlockInfo();
                nextBlockId = BlockCount() - 1;
                SaveCurrentBlock(BlockCount());

                //Get the source relation first
                var relation = GetRelation(relationId);
                var NTSrelation = GeometrySupport.ConvertOsmEntryToStoredElement(relation);
                bounds = NTSrelation.elementGeometry.EnvelopeInternal;
                var pgf = new PreparedGeometryFactory();
                boundsEntry = pgf.Create(NTSrelation.elementGeometry);

                //The boundsEntry will be used when checking geometry, and things that intersect will be processed and the rest excluded.                
                for (var block = nextBlockId; block >= 1; block--)
                {
                    if (block >= firstRelationBlock)
                        Log.WriteLog("Relation Block " + block);
                    else if (block >= firstWayBlock)
                        Log.WriteLog("Way Block " + block);
                    else
                        Log.WriteLog("Node Block " + block);
                    var geoData = GetGeometryFromBlock(block, false, false).Where(g => g != null).ToList();
                    writeTasks.Add(ProcessReaderResults(geoData));
                }
                Task.WaitAll(writeTasks.ToArray());
                Close();
                CleanupFiles();
                sw.Stop();
                Log.WriteLog("Area processed at " + DateTime.Now + ", session lasted " + sw.Elapsed);
                Log.WriteLog("Waiting for threaded tasks to finish.");
                return bounds;
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

        //public void debugArea(string filename, long areaId)
        //{
        //    Open(filename);
        //    IndexFile();
        //    //LoadBlockInfo();
        //    var block = relationFinder[areaId];

        //    PraxisCore.Singletons.SimplifyAreas = true; //Labrador Sea is huge. 12 MB by itself.
        //    var r = GetRelation(areaId);
        //    var r2 = GeometrySupport.ConvertOsmEntryToStoredElement(r);
        //    GeometrySupport.WriteSingleStoredElementToFile("labradorSea.json", r2);
        //    Close();
        //    CleanupFiles();
        //}

        //public void debugPerfTest(string filename)
        //{
        //    //testing feature interprester variances.
        //    Open(filename);
        //    IndexFile();
        //    var featureInterpreter = new PMFeatureInterpreter();
        //    var allRelations = new List<CompleteRelation>();
        //    foreach (var r in relationFinder)
        //    {
        //        try
        //        {
        //            var g = GetRelation(9428957);  //(r.Key); //gulf of st laurence
        //            TimeSpan runA, runB;

        //            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        //            sw.Start();
        //            var featureA = featureInterpreter.Interpret(g);
        //            sw.Stop();
        //            runA = sw.Elapsed;
        //            Log.WriteLog("Customized interpreter ran in " + runA);
        //            var check2 = GeometrySupport.SimplifyArea(featureA.First().Geometry);
        //            sw.Restart();
        //            var featureB = OsmSharp.Geo.FeatureInterpreter.DefaultInterpreter.Interpret(g); //mainline version, while i get my version dialed in for edge cases.
        //            sw.Stop();
        //            runB = sw.Elapsed;
        //            Log.WriteLog("Default interpreter ran in " + runB);
        //            Log.WriteLog("Change from using custom interpreter: " + (runB - runA));
        //            var a = 1;
        //        }
        //        catch (Exception ex)
        //        {
        //            //do nothing
        //        }
        //    }
        //}


        //Build the index for entries in this PBF file.
        private void IndexFile()
        {
            Log.WriteLog("Indexing file...");
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            fs.Position = 0;
            long blockCounter = 0;
            blockPositions = new Dictionary<long, long>(initialCapacity);
            blockSizes = new Dictionary<long, int>(initialCapacity);
            relationFinder = new ConcurrentDictionary<long, long>(initialConcurrency, initialCapacity);
            nodeFinder2 = new ConcurrentDictionary<long, Tuple<long, long>>(initialConcurrency, initialCapacity);
            wayFinder = new ConcurrentDictionary<long, long>(initialConcurrency, initialCapacity);

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

                    var group = pb2.primitivegroup[0]; //If i get a file with multiple PrimitiveGroups in a block, make this a ForEach loop instead.
                    if (group.ways.Count > 0)
                    {
                        var wMax = group.ways.Last().id; //.Max(w => w.id);
                        wayFinder.TryAdd(passedBC, wMax);
                        wayCounter++;
                    }
                    else if (group.relations.Count > 0)
                    {
                        relationCounter++;
                        foreach (var r in group.relations)
                        {
                            relationFinder.TryAdd(r.id, passedBC);
                        }
                    }
                    else
                    {
                        long minNode = 0;
                        long maxNode = 0;
                        if (group.dense != null)
                        {
                            minNode = group.dense.id[0];
                            maxNode = group.dense.id.Sum();
                            nodeFinder2.TryAdd(passedBC, new Tuple<long, long>(minNode, maxNode));
                        }
                    }
                });

                waiting.Add(tasked);
            }
            Task.WaitAll(waiting.ToArray());
            //this logic does require the wayIndex to be in blockID order, which they are (at least from Geofabrik).
            foreach (var w in wayFinder.OrderBy(w => w.Key))
            {
                wayFinderList.Add(Tuple.Create(w.Key, w.Value));
            }
            Log.WriteLog("Found " + blockCounter + " blocks. " + relationCounter + " relation blocks and " + wayCounter + " way blocks.");
            foreach (var entry in nodeFinder2)
            {
                nodeFinderList.Add(Tuple.Create(entry.Key, entry.Value.Item1, entry.Value.Item2));
            }
            SetOptimizationValues();
            sw.Stop();
            Log.WriteLog("File indexed in " + sw.Elapsed);
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
        private PrimitiveBlock GetBlockFromFile(long blockId)
        {
            byte[] thisblob1;
            lock (msLock)
            {
                long pos1 = blockPositions[blockId];
                int size1 = blockSizes[blockId];
                fs.Seek(pos1, SeekOrigin.Begin);
                thisblob1 = new byte[size1];
                fs.Read(thisblob1, 0, size1);
            }

            var ms2 = new MemoryStream(thisblob1);
            var b2 = Serializer.Deserialize<Blob>(ms2);
            var ms3 = new MemoryStream(b2.zlib_data);
            var dms2 = new ZlibStream(ms3, CompressionMode.Decompress);
            var pulledBlock = Serializer.Deserialize<PrimitiveBlock>(dms2);
            return pulledBlock;
        }

        /// <summary>
        /// Converts the byte array for a block into the PrimitiveBlock object.
        /// </summary>
        /// <param name="blockBytes">the bytes making up the block</param>
        /// <returns>the PrimitiveBlock object requested.</returns>
        private PrimitiveBlock DecodeBlock(byte[] blockBytes)
        {
            var ms2 = new MemoryStream(blockBytes);
            var b2 = Serializer.Deserialize<Blob>(ms2);
            var ms3 = new MemoryStream(b2.zlib_data);
            var dms2 = new ZlibStream(ms3, CompressionMode.Decompress);

            var pulledBlock = Serializer.Deserialize<PrimitiveBlock>(dms2);
            return pulledBlock;
        }

        /// <summary>
        /// Processes the requested relation into an OSMSharp CompleteRelation from the currently opened file
        /// </summary>
        /// <param name="relationId">the relation to load and process</param>
        /// <param name="ignoreUnmatched">if true, skip entries that don't get a TagParser match applied to them.</param>
        /// <returns>an OSMSharp CompleteRelation, or null if entries are missing, the elements were unmatched and ignoreUnmatched is true, or there were errors creating the object.</returns>
        private OsmSharp.Complete.CompleteRelation GetRelation(long relationId, bool ignoreUnmatched = false)
        {
            try
            {
                var relationBlockValues = relationFinder[relationId];
                PrimitiveBlock relationBlock = GetBlock(relationBlockValues);

                var relPrimGroup = relationBlock.primitivegroup[0];
                var rel = relPrimGroup.relations.FirstOrDefault(r => r.id == relationId);

                //sanity check - if this relation doesn't have inner or outer role members,
                //its not one i can process.
                bool canProcess = false;
                foreach (var role in rel.roles_sid)
                {
                    string roleType = System.Text.Encoding.UTF8.GetString(relationBlock.stringtable.s[role]);
                    if (roleType == "inner" || roleType == "outer")
                    {
                        canProcess = true; //I need at least one outer, and inners require outers.
                        break;
                    }
                }

                if (!canProcess || rel.keys.Count == 0) //I cant use untagged areas for anything in PraxisMapper.
                    return null;

                //If I only want elements that show up in the map, and exclude areas I don't currently match,
                //I have to knows my tags BEFORE doing the rest of the processing.
                OsmSharp.Complete.CompleteRelation r = new OsmSharp.Complete.CompleteRelation();
                r.Id = relationId;
                r.Tags = new OsmSharp.Tags.TagsCollection();

                for (int i = 0; i < rel.keys.Count(); i++)
                {
                    r.Tags.Add(new OsmSharp.Tags.Tag(System.Text.Encoding.UTF8.GetString(relationBlock.stringtable.s[(int)rel.keys[i]]), System.Text.Encoding.UTF8.GetString(relationBlock.stringtable.s[(int)rel.vals[i]])));
                }

                if (ignoreUnmatched)
                {
                    var tpe = TagParser.GetStyleForOsmWay(r.Tags);
                    if (tpe.name == TagParser.defaultStyle.name)
                        return null; //This is 'unmatched', skip processing this entry.
                }

                //Now get a list of block i know i need now.
                List<long> wayBlocks = new List<long>();

                //memIds is delta-encoded
                long idToFind = 0;
                for (int i = 0; i < rel.memids.Count; i++)
                {
                    idToFind += rel.memids[i];
                    Relation.MemberType typeToFind = rel.types[i];

                    switch (typeToFind)
                    {
                        case Relation.MemberType.NODE:
                            //The FeatureInterpreter doesn't use nodes from a relation
                            break;
                        case Relation.MemberType.WAY:
                            wayBlocks.Add(FindBlockKeyForWay(idToFind, wayBlocks));
                            wayBlocks = wayBlocks.Distinct().ToList();
                            break;
                        case Relation.MemberType.RELATION: //ignore meta-relations
                                                           //neededBlocks.Add(relationFinder[idToFind].Item1);
                            break;
                    }
                }

                //This makes sure we only load each element once. If a relation references an element more than once (it shouldnt)
                //this saves us from re-creating the same entry.
                Dictionary<long, OsmSharp.Complete.CompleteWay> loadedWays = new Dictionary<long, OsmSharp.Complete.CompleteWay>(8000);
                List<OsmSharp.Complete.CompleteRelationMember> crms = new List<OsmSharp.Complete.CompleteRelationMember>(8000);
                idToFind = 0;
                for (int i = 0; i < rel.memids.Count; i++)
                {
                    idToFind += rel.memids[i];
                    Relation.MemberType typeToFind = rel.types[i];
                    OsmSharp.Complete.CompleteRelationMember c = new OsmSharp.Complete.CompleteRelationMember();
                    c.Role = System.Text.Encoding.UTF8.GetString(relationBlock.stringtable.s[rel.roles_sid[i]]);
                    switch (typeToFind)
                    {
                        case Relation.MemberType.NODE:
                            break;
                        case Relation.MemberType.WAY:
                            if (!loadedWays.ContainsKey(idToFind))
                                loadedWays.Add(idToFind, GetWay(idToFind, false, wayBlocks, false));
                            c.Member = loadedWays[idToFind];
                            break;
                    }
                    crms.Add(c);
                }
                r.Members = crms.ToArray();

                //Some memory cleanup slightly early, in an attempt to free up RAM faster.
                loadedWays.Clear();
                loadedWays = null;
                rel = null;
                return r;
            }
            catch (Exception ex)
            {
                Log.WriteLog("relation failed:" + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Processes the requested way from the currently open file into an OSMSharp CompleteWay
        /// </summary>
        /// <param name="wayId">the way Id to process</param>
        /// <param name="skipUntagged">if true, skip this entry if it doesn't have any tags applied to it</param>
        /// <param name="hints">a list of currently loaded blocks to check before doing a full BTree search for entries</param>
        /// <param name="ignoreUnmatched">if true, returns null if this element's tags only match the default style.</param>
        /// <returns>the CompleteWay object requested, or null if skipUntagged or ignoreUnmatched checks skip this elements, or if there is an error processing the way</returns>
        private OsmSharp.Complete.CompleteWay GetWay(long wayId, bool skipUntagged, List<long> hints = null, bool ignoreUnmatched = false)
        {
            try
            {
                var wayBlockValues = FindBlockKeyForWay(wayId, hints);

                PrimitiveBlock wayBlock = GetBlock(wayBlockValues);
                var wayPrimGroup = wayBlock.primitivegroup[0];
                var way = wayPrimGroup.ways.FirstOrDefault(w => w.id == wayId);
                if (way == null)
                    return null; //way wasn't in the block it was supposed to be in.
                                 //finally have the core item

                if (skipUntagged && way.keys.Count == 0)
                    return null;

                //Now I have the data needed to fill in nodes for a way
                OsmSharp.Complete.CompleteWay finalway = new OsmSharp.Complete.CompleteWay();
                finalway.Id = wayId;
                finalway.Tags = new OsmSharp.Tags.TagsCollection(5); //Average is 3.

                //We always need to apply tags here, so we can either skip after (if IgnoredUmatched is set) or to pass along tag values correctly.
                for (int i = 0; i < way.keys.Count(); i++)
                    finalway.Tags.Add(new OsmSharp.Tags.Tag(System.Text.Encoding.UTF8.GetString(wayBlock.stringtable.s[(int)way.keys[i]]), System.Text.Encoding.UTF8.GetString(wayBlock.stringtable.s[(int)way.vals[i]])));

                if (ignoreUnmatched)
                {
                    if (TagParser.GetStyleForOsmWay(finalway.Tags).name == TagParser.defaultStyle.name)
                        return null; //don't process this one, we said not to load entries that aren't already in our style list.
                }


                //NOTES:
                //This gets all the entries we want from each node, then loads those all in 1 pass per referenced block.
                //This is significantly faster than doing a GetBlock per node when 1 block has mulitple entries
                //its a little complicated but a solid performance boost.
                long idToFind = 0; //more deltas 
                                   //blockId, nodeID
                List<Tuple<long, long>> nodesPerBlock = new List<Tuple<long, long>>(8000);
                List<long> distinctBlockIds = new List<long>(way.refs.Count); //Could make this a dictionary and tryAdd instead of add and distinct every time?
                for (int i = 0; i < way.refs.Count; i++)
                {
                    idToFind += way.refs[i];
                    var blockID = FindBlockKeyForNode(idToFind, distinctBlockIds);
                    distinctBlockIds.Add(blockID);
                    distinctBlockIds = distinctBlockIds.Distinct().ToList();
                    nodesPerBlock.Add(Tuple.Create(blockID, idToFind));
                }
                var nodesByBlock = nodesPerBlock.ToLookup(k => k.Item1, v => v.Item2);

                List<OsmSharp.Node> nodeList = new List<OsmSharp.Node>(8000);
                Dictionary<long, OsmSharp.Node> AllNodes = new Dictionary<long, OsmSharp.Node>(8000);
                foreach (var block in nodesByBlock)
                {
                    var someNodes = GetAllNeededNodesInBlock(block.Key, block.Distinct().OrderBy(b => b).ToArray());
                    if (someNodes == null)
                        return null; //throw new Exception("Couldn't load all nodes from a block");
                    foreach (var n in someNodes)
                        AllNodes.Add(n.Key, n.Value);
                }

                idToFind = 0;
                foreach (var node in way.refs)
                {
                    idToFind += node; //delta coding.
                    nodeList.Add(AllNodes[idToFind]);
                }

                finalway.Nodes = nodeList.ToArray();
                return finalway;
            }
            catch (Exception ex)
            {
                Log.WriteLog("GetWay failed: " + ex.Message + ex.StackTrace);
                return null; //Failed to get way, probably because a node didn't exist in the file.
            }
        }

        /// <summary>
        /// Returns the Nodes that have tags applied from a block.
        /// </summary>
        /// <param name="block">the block of Nodes to search through</param>
        /// <param name="ignoreUnmatched">if true, skip nodes that have tags that only match the default TaParser style.</param>
        /// <returns>a list of Nodes with tags, which may have a length of 0.</returns>
        private List<OsmSharp.Node> GetTaggedNodesFromBlock(PrimitiveBlock block, bool ignoreUnmatched = false)
        {
            List<OsmSharp.Node> taggedNodes = new List<OsmSharp.Node>(8000);
            var dense = block.primitivegroup[0].dense;

            //Shortcut: if dense.keys.count == 8000, there's no tagged nodes at all here (0 means 'no keys', and 8000 0's means every entry has no keys)
            if (dense.keys_vals.Count == 8000)
                return taggedNodes;

            //sort out tags ahead of time.
            int entryCounter = 0;
            List<Tuple<int, string, string>> idKeyVal = new List<Tuple<int, string, string>>();
            for (int i = 0; i < dense.keys_vals.Count; i++)
            {
                if (dense.keys_vals[i] == 0)
                {
                    entryCounter++;
                    continue;
                }
                //skip to next entry.
                idKeyVal.Add(
                    Tuple.Create(entryCounter,
                System.Text.Encoding.UTF8.GetString(block.stringtable.s[dense.keys_vals[i]]),
                System.Text.Encoding.UTF8.GetString(block.stringtable.s[dense.keys_vals[i + 1]])
                ));
                i++;
            }
            var decodedTags = idKeyVal.ToLookup(k => k.Item1, v => new OsmSharp.Tags.Tag(v.Item2, v.Item3));

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

                if (decodedTags[index].Count() == 0)
                    continue;

                //now, start loading keys/values
                OsmSharp.Tags.TagsCollection tc = new OsmSharp.Tags.TagsCollection();
                foreach (var t in decodedTags[index].ToList())
                    tc.Add(t);

                if (ignoreUnmatched)
                {
                    if (TagParser.GetStyleForOsmWay(tc) == TagParser.defaultStyle)
                        continue;
                }

                OsmSharp.Node n = new OsmSharp.Node();
                n.Id = nodeId;
                n.Latitude = DecodeLatLon(lat, block.lat_offset, block.granularity);
                n.Longitude = DecodeLatLon(lon, block.lon_offset, block.granularity);
                n.Tags = tc;

                //if bounds checking, drop nodes that aren't needed.
                if (bounds == null || (n.Latitude >= bounds.MinY && n.Latitude <= bounds.MaxY && n.Longitude >= bounds.MinX && n.Longitude <= bounds.MaxX))
                    taggedNodes.Add(n);
            }

            return taggedNodes;
        }

        /// <summary>
        /// Pulls out all requested nodes from a block. Significantly faster to pull all nodes per block this way than to run through the list for each node.
        /// </summary>
        /// <param name="blockId">the block to pull nodes out of</param>
        /// <param name="nodeIds">the IDs of nodes to load from this block</param>
        /// <returns>a Dictionary of the node ID and corresponding values.</returns>
        /// <exception cref="Exception">If a node isn't found in the expected block, an exception is thrown.</exception>
        private Dictionary<long, OsmSharp.Node> GetAllNeededNodesInBlock(long blockId, long[] nodeIds)
        {
            Dictionary<long, OsmSharp.Node> results = new Dictionary<long, OsmSharp.Node>(nodeIds.Length);
            int arrayIndex = 0;

            var block = GetBlock(blockId);
            var group = block.primitivegroup[0];

            int index = -1;
            long nodeCounter = 0;
            long latDelta = 0;
            long lonDelta = 0;
            var denseIds = group.dense.id;
            var dLat = group.dense.lat;
            var dLon = group.dense.lon;
            while (results.Count < nodeIds.Length)
            {
                index++;
                if (index == 8000)
                    throw new Exception("Node not found in indexed node!");
                //We didn't find all the nodes we were looking for.

                nodeCounter += denseIds[index];
                latDelta += dLat[index];
                lonDelta += dLon[index];

                if (nodeIds[arrayIndex] == nodeCounter)
                {
                    OsmSharp.Node filled = new OsmSharp.Node();
                    filled.Id = nodeCounter;
                    filled.Latitude = DecodeLatLon(latDelta, block.lat_offset, block.granularity);
                    filled.Longitude = DecodeLatLon(lonDelta, block.lon_offset, block.granularity);
                    results.Add(nodeCounter, filled);
                    arrayIndex++;
                }
            }
            return results;
        }

        /// <summary>
        /// Determine if a block is expected to have the given node by its nodeID, using its indexed values
        /// </summary>
        /// <param name="key">the NodeId to check for in this block</param>
        /// <param name="value">the Tuple of min and max node IDs in a block.</param>
        /// <returns>true if key is between the 2 Tuple values, or false ifnot.</returns>
        private bool NodeHasKey(long key, Tuple<long, long> value)
        {
            //key is block id
            //value is the tuple list. 1 is min, 2 is max.
            if (value.Item1 > key) //this node's minimum is larger than our node, skip
                return false;

            if (value.Item2 < key) //this node's maximum is smaller than our node, skip
                return false;
            return true;
        }

        /// <summary>
        /// Determine which node in the file has the given Node, using a BTree search on the index.
        /// </summary>
        /// <param name="nodeId">The node to find in the currently opened file</param>
        /// <param name="hints">a list of blocks to check first, assuming that nodes previously searched are likely to be near each other. Ignored if more than 20 entries are in the list. </param>
        /// <returns>the block ID containing the requested node</returns>
        /// <exception cref="Exception">Throws an exception if the nodeId isn't found in the current file.</exception>
        private long FindBlockKeyForNode(long nodeId, List<long> hints = null) //BTree
        {
            //This is the most-called function in this class, and therefore the most performance-dependent.
            //As it turns out, the range of node IDs involved WON'T overlap, so I can b-tree search this

            //Hints is a list of blocks we're already found in the relevant way. Odds are high that
            //any node I need to find is in the same block as another node I've found.
            //This should save a lot of time searching the list when I have already found some blocks
            //and shoudn't waste too much time if it isn't in a block already found.
            if (hints != null && hints.Count() < nodeHintsMax) //skip hints if the BTree search is fewer checks.
            {
                foreach (var h in hints)
                {
                    var entry = nodeFinder2[h];
                    if (NodeHasKey(nodeId, entry))
                        return h;
                }
            }

            //ways will hit a couple thousand blocks, nodes hit hundred of thousands of blocks.
            //This might help performance on ways, but will be much more noticeable on nodes.
            int min = 0;
            int max = nodeFinderTotal;
            int current = startNodeBtreeIndex;

            while (min != max)
            {
                var check = nodeFinderList[current];
                if (check.Item2 > nodeId) //this node's minimum is larger than our node, shift up
                    max = current;
                else if (check.Item3 < nodeId) //this node's maximum is smaller than our node, shift down.
                    min = current;
                else
                    return check.Item1;

                current = (min + max) / 2;
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

        private long FindBlockKeyForWay(long wayId, List<long> hints) //BTree
        {
            if (hints != null && hints.Count() < wayHintsMax) //skip hints if the BTree search is fewer checks.
                foreach (var h in hints)
                {
                    //we can check this, but we need to look at the previous block too.
                    if (wayFinder[h] >= wayId && (h == firstWayBlock || wayFinder[h - 1] < wayId))
                        return h;
                }

            int min = 0;
            int max = wayFinderTotal;
            int current = startWayBtreeIndex;
            while (min != max)
            {
                var check = wayFinderList[current];
                if (check.Item2 < wayId) //This max is below our way, shift up
                {
                    min = current;
                }
                else if (check.Item2 >= wayId) //this max is over our way, check previous block if this one is correct OR shift down if not
                {
                    if (current == 0 || wayFinderList[current - 1].Item2 < wayId) //our way is below current max, above previous max, this is the block we want
                        return check.Item1;
                    else
                        max = current;
                }

                current = (min + max) / 2;
            }

            //couldnt find this way
            throw new Exception("Way Not Found");
        }

        /// <summary>
        /// Puts loading a Relation into its own thread so the rest of the file could advance instead of waiting on one object. Exports directly to the destination JSON file upon processing. Not currently used in the process, since this task can reload blocks previously discarded and cause RAM related issues.
        /// </summary>
        /// <param name="elementId">the relation to convert on its own</param>
        /// <param name="saveFilename">the destination file to save the JSON formatted StoredOsmElement to. </param>
        /// <returns>the Task handling the processing.</returns>
        public Task GetBigRelationSolo(long elementId, string saveFilename)
        {
            //For splitting off a single, long-running element into its own whole thread.
            try
            {
                var t = Task.Run(() =>
                {
                    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                    sw.Start();
                    Log.WriteLog("Processing large relation " + elementId + " separately at " + DateTime.Now);
                    var relation = GetRelation(elementId);
                    if (relation == null)
                    {
                        Log.WriteLog("Large relation " + elementId + " not suitable for processing, exiting standalone task.");
                        return;
                    }
                    Log.WriteLog("Large relation " + elementId + " created at " + DateTime.Now);
                    var md = GeometrySupport.ConvertOsmEntryToStoredElement(relation);
                    if (md == null)
                    {
                        Log.WriteLog("Error: relation " + relation.Id + " failed to convert to StoredOSmElement");
                        return;
                    }
                    var recordVersion = new StoredOsmElementForJson(md.id, md.name, md.sourceItemID, md.sourceItemType, md.elementGeometry.AsText(), string.Join("~", md.Tags.Select(t => t.Key + "|" + t.Value)), md.IsGameElement, md.IsUserProvided, md.IsGenerated);
                    if (recordVersion == null)
                    {
                        Log.WriteLog("Error: relation " + relation.Id + " failed to convert to StoredOSmElementForJson");
                        return;
                    }
                    var text = JsonSerializer.Serialize(recordVersion, typeof(StoredOsmElementForJson));
                    if (text == null)
                    {
                        Log.WriteLog("Error: relation " + relation.Id + " failed to convert to Json.");
                        return;
                    }
                    Log.WriteLog("Large relation " + elementId + " converted at " + DateTime.Now);
                    lock (fileLock)
                    {
                        System.IO.File.AppendAllText(saveFilename, text);
                        System.IO.File.AppendAllText(saveFilename, Environment.NewLine);
                    }
                    sw.Stop();
                    Log.WriteLog("Processed large relation " + elementId + " from start to finish in " + sw.Elapsed);
                    return;
                });
                return t;
            }
            catch (Exception ex)
            {
                Log.WriteLog("error getting single relation: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Processes all entries in a PBF block for use in a PraxisMapper server.
        /// </summary>
        /// <param name="blockId">the block to process</param>
        /// <param name="onlyTagMatchedEntries">if true, skips elements that match the default style for the TagParser style set</param>
        /// <param name="infileProcess">if true, save results to a MariaDB Infile formatted text file, allowing for dramatically faster loading than the normal JSON process.</param>
        /// <param name="exportNodesToJson">if true, save results in JSON format to a text file for loading into any database.</param>
        /// <returns>A ConcurrentBag of OSMSharp CompleteGeo objects.</returns>
        public ConcurrentBag<OsmSharp.Complete.ICompleteOsmGeo> GetGeometryFromBlock(long blockId, bool onlyTagMatchedEntries = false, bool infileProcess = false, bool exportNodesToJson = false)
        {
            //This grabs the chosen block, populates everything in it to an OsmSharp.Complete object and returns that list
            try
            {
                var block = GetBlock(blockId);
                ConcurrentBag<OsmSharp.Complete.ICompleteOsmGeo> results = new ConcurrentBag<OsmSharp.Complete.ICompleteOsmGeo>();
                //Attempting to clear up some memory slightly faster, but this should be redundant.
                relList.Clear();
                relList = null;
                relList = new ConcurrentBag<Task>();
                foreach (var primgroup in block.primitivegroup)
                {
                    if (primgroup.relations != null && primgroup.relations.Count() > 0)
                    {
                        //Some relation blocks can hit 22GB of RAM on their own. Dividing relation blocks into 4 pieces to help minimize that.
                        var splitcount = primgroup.relations.Count() / 4;
                        for (int i = 0; i < 5; i++) //5 means we won't miss any, just in case splitcount leaves us with 3 remainder entries.
                        {
                            var toProcess = primgroup.relations.Skip(splitcount * i).Take(splitcount).ToList();
                            foreach (var r in toProcess)
                                relList.Add(Task.Run(() => results.Add(GetRelation(r.id, onlyTagMatchedEntries))));

                            Task.WaitAll(relList.ToArray());
                            activeBlocks.Clear(); //Dump them all out of RAM, see how this affects performance. At most a block gets read 3 times more than it would have before.
                        }
                    }
                    else if (primgroup.ways != null && primgroup.ways.Count() > 0)
                    {
                        List<long> hint = new List<long>() { blockId };
                        foreach (var r in primgroup.ways.OrderByDescending(w => w.refs.Count())) //Ordering should help consistency in runtime, though it offers little other benefit.
                        {
                            relList.Add(Task.Run(() => results.Add(GetWay(r.id, true, hint, onlyTagMatchedEntries))));
                        }
                    }
                    else
                    {
                        //Useful node lists are so small, they lose performance from splitting each step into 1 task per entry.
                        //Inline all that here as one task and return null to skip the rest. But this doesn't work if I'm writing to a DB.
                        //writeTasks.Add(Task.Run(() =>
                        relList.Add(Task.Run(() =>
                        {
                            try
                            {
                                var nodes = GetTaggedNodesFromBlock(block, onlyTagMatchedEntries);
                                foreach (var n in nodes)
                                    results.Add(n);

                                if (infileProcess)
                                {
                                    var convertednodes = nodes.Select(n => GeometrySupport.ConvertOsmEntryToStoredElement(n)).ToList();
                                    StringBuilder geomSB = new StringBuilder();
                                    StringBuilder tagsSB = new StringBuilder();
                                    foreach (var md in convertednodes)
                                    {
                                        if (md != null)
                                        {
                                            geomSB.Append(md.name).Append("\t").Append(md.sourceItemID).Append("\t").Append(md.sourceItemType).Append("\t").Append(md.elementGeometry.AsText()).Append("\t0.000125\r\n");
                                            foreach (var t in md.Tags)
                                                tagsSB.Append(md.sourceItemID).Append("\t").Append(md.sourceItemType).Append("\t").Append(t.Key).Append("\t").Append(t.Value.Replace("\r", "").Replace("\n", "")).Append("\r\n"); //ensure line endings are consistent.
                                        }
                                    }
                                    lock (geomFileLock)
                                        geomFileStream.Write(geomSB);
                                    lock (tagsFileLock)
                                        tagsFileStream.Write(tagsSB);
                                }
                                else if (exportNodesToJson)
                                {
                                    var convertednodes = nodes.Select(n => GeometrySupport.ConvertOsmEntryToStoredElement(n)).ToList();
                                    var classForJson = convertednodes.Where(c => c != null).Select(md => new StoredOsmElementForJson(md.id, md.name, md.sourceItemID, md.sourceItemType, md.elementGeometry.AsText(), string.Join("~", md.Tags.Select(t => t.Key + "|" + t.Value)), md.IsGameElement, md.IsUserProvided, md.IsGenerated)).ToList();
                                    var textLines = classForJson.Select(c => JsonSerializer.Serialize(c, typeof(StoredOsmElementForJson))).ToList();
                                    lock (fileLock)
                                        System.IO.File.AppendAllLines(outputPath + System.IO.Path.GetFileNameWithoutExtension(fi.Name) + ".json", textLines);
                                }
                                return;
                            }
                            catch (Exception ex)
                            {
                                Log.WriteLog("Processing node failed: " + ex.Message);
                                return;
                            }
                        }));
                    }
                }

                Task.WaitAll(relList.ToArray());

                //Moved this logic here to free up RAM by removing blocks once we're done reading data from the hard drive. Should result in fewer errors at the ProcessReaderResults step.
                //Slightly more complex: only remove blocks we didn't access last call. saves some serialization effort. Small RAM trade for 30% speed increase.
                foreach (var blockRead in activeBlocks)
                {
                    if (!accessedBlocks.ContainsKey(blockRead.Key))
                        activeBlocks.TryRemove(blockRead);

                }
                accessedBlocks.Clear();

                var count = (block.primitivegroup[0].relations?.Count > 0 ? block.primitivegroup[0].relations.Count :
                    block.primitivegroup[0].ways?.Count > 0 ? block.primitivegroup[0].ways.Count :
                    block.primitivegroup[0].dense.id.Count);

                return results;
            }
            catch (Exception ex)
            {
                Log.WriteLog("error getting geometry: " + ex.Message);
                return null;
            }
        }

        //Taken from OsmSharp (MIT License)
        /// <summary>
        /// Turns a PBF's dense stored data into a standard latitude or longitude value in degrees.
        /// </summary>
        /// <param name="valueOffset">the valueOffset for the block data is loaded from</param>
        /// <param name="offset">the offset for the node currently loaded</param>
        /// <param name="granularity">the granularity value of the block data is loaded from</param>
        /// <returns>a double represeting the lat or lon value for the given dense values</returns>
        private static double DecodeLatLon(long valueOffset, long offset, long granularity)
        {
            return .000000001 * (offset + (granularity * valueOffset));
        }
        //end OsmSharp copied functions.

        /// <summary>
        /// Saves the indexes created for the currently opened file to their own files, so that they can be read instead of created if processing needs to resume later.
        /// </summary>
        private void SaveBlockInfo()
        {
            string filename = outputPath + fi.Name + ".blockinfo";
            //now deserialize
            string[] data = new string[blockPositions.Count()];
            for (int i = 0; i < data.Count(); i++)
            {
                data[i] = i + ":" + blockPositions[i] + ":" + blockSizes[i];
            }
            System.IO.File.WriteAllLines(filename, data);

            filename = outputPath + fi.Name + ".relationIndex";
            data = new string[relationFinder.Count()];
            int j = 0;
            foreach (var wf in relationFinder)
            {
                data[j] = wf.Key + ":" + wf.Value;
                j++;
            }
            System.IO.File.WriteAllLines(filename, data);

            filename = outputPath + fi.Name + ".wayIndex";
            data = new string[wayFinderTotal];
            j = 0;
            foreach (var wf in wayFinderList)
            {
                data[j] = wf.Item1 + ":" + wf.Item2;
                j++;
            }
            System.IO.File.WriteAllLines(filename, data);

            filename = outputPath + fi.Name + ".nodeIndex";
            data = new string[nodeFinder2.Count()];
            j = 0;
            foreach (var wf in nodeFinder2)
            {
                data[j] = wf.Key + ":" + wf.Value.Item1 + ":" + wf.Value.Item2;
                j++;
            }
            System.IO.File.WriteAllLines(filename, data);
        }

        /// <summary>
        /// Loads indexed data from a previous run from file, to skip reprocessing the entire file.
        /// </summary>
        private void LoadBlockInfo()
        {
            try
            {
                string filename = outputPath + fi.Name + ".blockinfo";
                string[] data = System.IO.File.ReadAllLines(filename);
                blockPositions = new Dictionary<long, long>(data.Length);
                blockSizes = new Dictionary<long, int>(data.Length);

                for (int i = 0; i < data.Count(); i++)
                {
                    string[] subdata = data[i].Split(":");
                    blockPositions[i] = long.Parse(subdata[1]);
                    blockSizes[i] = int.Parse(subdata[2]);
                }

                filename = outputPath + fi.Name + ".relationIndex";
                data = System.IO.File.ReadAllLines(filename);
                foreach (var line in data)
                {
                    string[] subData2 = line.Split(":");
                    relationFinder.TryAdd(long.Parse(subData2[0]), long.Parse(subData2[1]));
                }

                filename = outputPath + fi.Name + ".wayIndex";
                data = System.IO.File.ReadAllLines(filename);
                foreach (var line in data)
                {
                    string[] subData2 = line.Split(":");
                    wayFinder.TryAdd(long.Parse(subData2[0]), long.Parse(subData2[1]));
                    wayFinderList.Add(Tuple.Create(long.Parse(subData2[0]), long.Parse(subData2[1])));
                }

                filename = outputPath + fi.Name + ".nodeIndex";
                data = System.IO.File.ReadAllLines(filename);
                foreach (var line in data)
                {
                    string[] subData2 = line.Split(":");
                    nodeFinder2.TryAdd(long.Parse(subData2[0]), Tuple.Create(long.Parse(subData2[1]), long.Parse(subData2[2])));
                }

                //I never use NodeFinder2 with the key, its always iterated over. It should be a list or a sorted concurrent entry
                foreach (var entry in nodeFinder2)
                {
                    nodeFinderList.Add(Tuple.Create(entry.Key, entry.Value.Item1, entry.Value.Item2));
                }
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
            nodeFinderTotal = nodeFinderList.Count();
            wayFinderTotal = wayFinderList.Count();
            firstWayBlock = wayFinder.Keys.Min();
            firstRelationBlock = relationFinder.Values.Min();
            startNodeBtreeIndex = nodeFinderTotal / 2;
            startWayBtreeIndex = wayFinderTotal / 2;
            nodeHintsMax = (int)Math.Log2(nodeFinderTotal);
            wayHintsMax = (int)Math.Log2(wayFinderTotal);
        }

        /// <summary>
        /// Saves the currently completed block to a file, so we can resume without reprocessing existing data if needed.
        /// </summary>
        /// <param name="blockID">the block most recently processed</param>
        private void SaveCurrentBlock(long blockID)
        {
            string filename = outputPath + fi.Name + ".progress";
            System.IO.File.WriteAllText(filename, blockID.ToString());
        }

        //Loads the most recently completed block from a file to resume without doing duplicate work.
        private long FindLastCompletedBlock()
        {
            string filename = outputPath + fi.Name + ".progress";
            long blockID = long.Parse(System.IO.File.ReadAllText(filename));
            return blockID;
        }

        /// <summary>
        /// Delete indexes and progress file.
        /// </summary>
        private void CleanupFiles()
        {
            try
            {
                foreach (var file in System.IO.Directory.EnumerateFiles(outputPath, "*.blockinfo"))
                    System.IO.File.Delete(file);

                foreach (var file in System.IO.Directory.EnumerateFiles(outputPath, "*.relationIndex"))
                    System.IO.File.Delete(file);

                foreach (var file in System.IO.Directory.EnumerateFiles(outputPath, "*.nodeIndex"))
                    System.IO.File.Delete(file);

                foreach (var file in System.IO.Directory.EnumerateFiles(outputPath, "*.wayIndex"))
                    System.IO.File.Delete(file);

                foreach (var file in System.IO.Directory.EnumerateFiles(outputPath, "*.progress"))
                    System.IO.File.Delete(file);
            }
            catch (Exception ex)
            {
                Log.WriteLog("Error cleaning up files: " + ex.Message);
            }
        }

        /// <summary>
        /// Called to display periodic performance summaries on the console while a file is being processed.
        /// </summary>
        public void ShowWaitInfo()
        {
            waitInfoTask = Task.Run(() =>
            {
                while (true)
                {
                    Log.WriteLog("Current stats:");
                    Log.WriteLog("Blocks completed this run: " + timeList.Count());
                    Log.WriteLog("Long-running/writing pending: " + writeTasks.Where(w => !w.IsCompleted).Count());
                    Log.WriteLog("Processing tasks: " + relList.Where(r => !r.IsCompleted).Count());
                    if (timeList.Count > 0)
                    {
                        Log.WriteLog("Average time per block: " + timeList.Average(t => t.TotalSeconds) + " seconds");
                    }
                    System.Threading.Thread.Sleep(60000);
                }
            }, token);
        }

        public void EndWaitInfoTask()
        {
            tokensource.Cancel();
        }

        /// <summary>
        /// Take a list of OSMSharp CompleteGeo items, and convert them into PraxisMapper's StoredOsmElement objects.
        /// </summary>
        /// <param name="items">the OSMSharp CompleteGeo items to convert</param>
        /// <param name="saveFilename">The filename to save data to. Ignored if saveToDB is true</param>
        /// <param name="saveToDb">If true, insert the items directly to the database instead of exporting to a file as JSON elements.</param>
        /// <param name="onlyTagMatchedElements">if true, only loads in elements that dont' match the default entry for a TagParser style set</param>
        /// <returns>the Task handling the conversion process</returns>
        public Task ProcessReaderResults(IEnumerable<OsmSharp.Complete.ICompleteOsmGeo> items)
        {
            //This one is easy, we just dump the geodata to the file.
            string saveFilename = outputPath + System.IO.Path.GetFileNameWithoutExtension(fi.Name) + ".json";
            ConcurrentBag<StoredOsmElement> elements = new ConcurrentBag<StoredOsmElement>();
            DateTime startedProcess = DateTime.Now;

            if (items == null || items.Count() == 0)
                return null;

            relList = new ConcurrentBag<Task>();
            foreach (var r in items)
            {
                if (r != null)
                    relList.Add(Task.Run(() => { var e = GeometrySupport.ConvertOsmEntryToStoredElement(r); if (e != null) elements.Add(e); }));
            }
            Task.WaitAll(relList.ToArray());
            relList = new ConcurrentBag<Task>();

            if (onlyMatchedAreas)
                elements = new ConcurrentBag<StoredOsmElement>(elements.Where(e => TagParser.GetStyleForOsmWay(e.Tags).name != TagParser.defaultStyle.name));

            if (boundsEntry != null)
                elements = new ConcurrentBag<StoredOsmElement>(elements.Where(e => boundsEntry.Intersects(e.elementGeometry)));

            if (saveToDB)
            {
                var splits = elements.AsEnumerable().SplitListToMultiple(4);
                List<Task> lt = new List<Task>();
                foreach (var list in splits)
                    lt.Add(Task.Run(() => { var db = new PraxisContext(); db.ChangeTracker.AutoDetectChangesEnabled = false; db.StoredOsmElements.AddRange(list); db.SaveChanges(); }));
                Task.WaitAll(lt.ToArray());

                return null;
            }
            else
            {
                if (saveToJson)
                {
                    //ConcurrentBag<string> results = new ConcurrentBag<string>();
                    StringBuilder jsonSB = new StringBuilder(50000000); //50MB of JsonData for a block is a good starting buffer.
                    foreach (var md in elements.Where(e => e != null))
                    {
                        //relList.Add(Task.Run(() =>
                        //{
                        var recordVersion = new StoredOsmElementForJson(md.id, md.name, md.sourceItemID, md.sourceItemType, md.elementGeometry.AsText(), string.Join("~", md.Tags.Select(t => t.Key + "|" + t.Value)), md.IsGameElement, md.IsUserProvided, md.IsGenerated);
                        var test = JsonSerializer.Serialize(recordVersion, typeof(StoredOsmElementForJson));
                        jsonSB.Append(test).Append(Environment.NewLine);
                        //}));
                    }
                    //Task.WaitAll(relList.ToArray());

                    //var monitorTask = System.Threading.Tasks.Task.Run(() =>
                    //{
                        try
                        {
                            //System.Diagnostics.Stopwatch sw2 = new System.Diagnostics.Stopwatch();
                            //sw2.Start();
                            lock (fileLock)
                                jsonFileStream.Write(jsonSB);

                            //sw2.Stop();
                            //Log.WriteLog("Data written to disk in" + sw2.Elapsed);
                        }
                        catch (Exception ex)
                        {
                            Log.WriteLog("Error writing data to disk:" + ex.Message);
                        }
                    //});

                    //return monitorTask;
                    return null;
                }
                else if (saveToInfile)
                {
                    //It's much faster to use StringBuilders here than the string arrays that were previously here.
                    //Starts with 50MB allocated in each 2 stringBuilders to minimize reallocations.
                    StringBuilder geometryBuilds = new StringBuilder(50000000);
                    StringBuilder tagBuilds = new StringBuilder(50000000);
                    //TODO: I might need to assign a PrivacyID at this step, since MariaDB has that saved in the entities as a char(32)
                    foreach (var md in elements.Where(e => e != null))
                    {
                        geometryBuilds.Append(md.name).Append("\t").Append(md.sourceItemID).Append("\t").Append(md.sourceItemType).Append("\t").Append(md.elementGeometry.AsText()).Append("\t").Append(md.elementGeometry.Length).Append(Environment.NewLine);
                        foreach (var t in md.Tags)
                            tagBuilds.Append(md.sourceItemID).Append("\t").Append(md.sourceItemType).Append("\t").Append(t.Key).Append("\t").Append(t.Value.Replace("\r", "").Replace("\n", "")).Append(Environment.NewLine); //Might also need to sanitize / and ' ?
                    }

                    //var monitorTask = Task.Run(() =>
                    //{
                        try
                        {
                            lock (geomFileLock)
                                geomFileStream.Write(geometryBuilds);
                            lock (tagsFileLock)
                                tagsFileStream.Write(tagBuilds);
                            //Log.WriteLog("Data written to disk");
                        }
                        catch (Exception ex)
                        {
                            Log.WriteLog("Error writing data to disk:" + ex.Message);
                        }
                    //});
                }

                return null; //some invalid options were passed and we didnt run through anything.
            }
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
                Open(filename);
                LoadBlockInfo();
                nextBlockId = 0;
                if (relationFinder.Count == 0)
                {
                    IndexFile();
                    SaveBlockInfo();
                    SaveCurrentBlock(BlockCount());
                }
                nextBlockId = BlockCount() - 1;

                if (displayStatus)
                    ShowWaitInfo();

                var relation = GetRelation(relationId);
                Close();
                //CleanupFiles();
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
                Open(filename);
                LoadBlockInfo();
                nextBlockId = 0;
                if (relationFinder.Count == 0)
                {
                    IndexFile();
                    SaveBlockInfo();
                    SaveCurrentBlock(BlockCount());
                }
                nextBlockId = BlockCount() - 1;

                if (displayStatus)
                    ShowWaitInfo();

                var way = GetWay(wayId, false);
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
    }
}
