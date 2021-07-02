using System;
using System.IO;
using System.Collections.Generic;
using ProtoBuf;
using Ionic.Zlib;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using static CoreComponents.DbTables;
using CoreComponents.Support;
using System.Text.Json;
using NetTopologySuite.Noding;
using OsmSharp.Complete;
using System.ComponentModel.DataAnnotations;

namespace CoreComponents.PbfReader
{
    public class PbfReader
    {
        //The 5th generation of logic for pulling geometry out of a pbf file. This one is written specfically for PraxisMapper, and
        //doesn't depend on OsmSharp for reading the raw data now. OsmSharp's still used for object types and the FeatureConverter. 

        //Primary function:
        //ProcessFile(filename) should do everything automatically and allow resuming if you stop the app.

        FileInfo fi;
        FileStream fs;

        //<osmId, blockId>
        ConcurrentDictionary<long, long> relationFinder = new ConcurrentDictionary<long, long>();

        //this is blockId, <minNode, maxNode>.
        ConcurrentDictionary<long, Tuple<long, long>> nodeFinder2 = new ConcurrentDictionary<long, Tuple<long, long>>();
        ConcurrentDictionary<long, Tuple<long, long>> nodeFinder2Reverse = new ConcurrentDictionary<long, Tuple<long, long>>();
        //<blockId, maxWayId> since ways are sorted in order.
        Dictionary<long, long> wayFinder2 = new Dictionary<long, long>(); //This one uses up less memory, but takes longer for bigger files because it needs to iterate them like a list.
        ConcurrentDictionary<long, long> exactWayFinder3 = new ConcurrentDictionary<long, long>(); //Stops the CPU creep but eats a lot more RAM, and currently throws some missing value errors.

        Dictionary<long, long> blockPositions = new Dictionary<long, long>();
        Dictionary<long, int> blockSizes = new Dictionary<long, int>();

        ConcurrentDictionary<long, PrimitiveBlock> activeBlocks = new ConcurrentDictionary<long, PrimitiveBlock>();
        ConcurrentDictionary<long, bool> accessedBlocks = new ConcurrentDictionary<long, bool>();

        private PrimitiveBlock _block = new PrimitiveBlock();
        private BlobHeader _header = new BlobHeader();

        object msLock = new object(); //reading blocks from disk.
        object fileLock = new object(); //Writing to json file

        public string outputPath = "";
        long nextBlockId = 0;
        long firstWayBlock = 0;

        ConcurrentBag<Task> writeTasks = new ConcurrentBag<Task>(); //Writing to json file, and long-running relation processing
        ConcurrentBag<Task> relList = new ConcurrentBag<Task>(); //Individual, smaller tasks.
        ConcurrentBag<TimeSpan> timeList = new ConcurrentBag<TimeSpan>();

        //Should try and monitor everything from one master task.

        //for reference. These are likely to be lost if the application dies partway through processing, since these sit outside the general block-by-block plan.
        private HashSet<long> knownSlowRelations = new HashSet<long>() {
            9488835, //Labrador Sea. 25,000 ways. Stack Overflows on converting to CompleteRelation through defaultFeatureInterpreter.
            1205151, //Lake Huron, 14,000 ways. Can Stack overflow joining rings.
            148838, //United States. 1029 members but a very large geographic area
            9428957, //Gulf of St. Lawrence. 11,000 ways. Can finish processing, so it's somewhere between 11k and 14k that the stack overflow hits.
            4039900, //Lake Erie is 1100 ways, takes ~56 seconds start to finish.
        };

        //lazy optimization: when to search a reversed list of nodes;
        long switchPoint = 0;

        public bool displayStatus = true;

        public long BlockCount()
        {
            return blockPositions.Count();
        }

        private void Open(string filename)
        {
            fi = new FileInfo(filename);
            fs = File.OpenRead(filename);

            Serializer.PrepareSerializer<PrimitiveBlock>();
            Serializer.PrepareSerializer<Blob>();
        }

        private void Close()
        {
            fs.Close();
            fs.Dispose();
        }

        public void ProcessFile(string filename, bool saveToDb = false)
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

                for (var block = nextBlockId; block > 0; block--)
                {
                    long thisBlockId = block;
                    var geoData = GetGeometryFromBlock(thisBlockId);
                    //There are large relation blocks where you can see how much time is spent writing them or waiting for one entry to
                    //process as the apps drops to a single thread in use, but I can't do much about those if I want to be able to resume a process.
                    if (geoData != null) //This process function is sufficiently parallel that I don't want to throw it off to a Task. The only sequential part is writing the data to the file, and I need that to keep accurate track of which blocks have beeen written to the file.
                    {
                        var wt = ProcessReaderResults(geoData, outputPath + System.IO.Path.GetFileNameWithoutExtension(filename) + ".json", saveToDb);
                        if (wt != null)
                            writeTasks.Add(wt);
                    }
                    SaveCurrentBlock(block);                        
                }

                Log.WriteLog("Waiting on " + writeTasks.Where(w => !w.IsCompleted).Count() + " additional tasks");
                Task.WaitAll(writeTasks.ToArray());
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

        public void debugArea(string filename, long areaId)
        {
            Open(filename);
            IndexFile();
            //LoadBlockInfo();
            var block = relationFinder[areaId];

            CoreComponents.Singletons.SimplifyAreas = true; //Labrador Sea is huge. 12 MB by itself.
            var r = GetRelation(areaId);
            var r2 = GeometrySupport.ConvertOsmEntryToStoredElement(r);
            GeometrySupport.WriteSingleStoredElementToFile("labradorSea.json", r2);
            Close();
            CleanupFiles();
        }

        public void debugPerfTest(string filename)
        {
            //testing feature interprester variances.
            Open(filename);
            IndexFile();
            var featureInterpreter = new PMFeatureInterpreter();
            var allRelations = new List<CompleteRelation>();
            foreach (var r in relationFinder)
            {
                try
                {
                    var g = GetRelation(9428957);  //(r.Key);
                    TimeSpan runA, runB;

                    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                    sw.Start();
                    var featureA = featureInterpreter.Interpret(g); //OsmSharp.Geo.FeatureInterpreter.DefaultInterpreter.Interpret(g); //Changed while waiting for bugfixes
                    sw.Stop();
                    runA = sw.Elapsed;
                    Log.WriteLog("Customized interpreter ran in " + runA);
                    var check2 = GeometrySupport.SimplifyArea(featureA.First().Geometry);
                    sw.Restart();
                    var featureB = OsmSharp.Geo.FeatureInterpreter.DefaultInterpreter.Interpret(g); //mainline version, while i get my version dialed in for edge cases.
                    sw.Stop();
                    runB = sw.Elapsed;
                    Log.WriteLog("Default interpreter ran in " + runB);
                    Log.WriteLog("Change from using custom interpreter: " + (runB - runA));
                    var a = 1;
                }
                catch(Exception ex)
                {
                    //do nothing
                }
            }

        }

        private void IndexFile()
        {
            Log.WriteLog("Indexing file...");
            fs.Position = 0;
            long blockCounter = 0;
            blockPositions = new Dictionary<long, long>();
            blockSizes = new Dictionary<long, int>();
            relationFinder = new ConcurrentDictionary<long, long>();
            nodeFinder2 = new ConcurrentDictionary<long, Tuple<long, long>>();
            wayFinder2 = new Dictionary<long, long>();

            BlobHeader bh = new BlobHeader();
            Blob b = new Blob();

            HeaderBlock hb = new HeaderBlock();
            PrimitiveBlock pb = new PrimitiveBlock();

            //Only one OsmHeader, at the start
            Serializer.MergeWithLengthPrefix(fs, bh, PrefixStyle.Fixed32BigEndian);
            hb = Serializer.Deserialize<HeaderBlock>(fs, length: bh.datasize); //only one of these per file    
            blockPositions.Add(0, fs.Position);
            blockSizes.Add(0, bh.datasize);

            List<Task> waiting = new List<Task>();
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
                var tasked = Task.Run(() =>
                {
                    var pb2 = DecodeBlock(thisblob);

                    var group = pb2.primitivegroup[0]; //If i get a file with multiple PrimitiveGroups in a block, make this a ForEach loop instead.
                    if (group.ways.Count > 0)
                    {
                        //foreach (var w in group.ways)
                        //{
                        //    exactWayFinder3.TryAdd(w.id, passedBC);
                        //}

                        var wMax = group.ways.Max(w => w.id);
                        if (!wayFinder2.TryAdd(passedBC, wMax))
                            Log.WriteLog("ERROR: failed to add block " + passedBC + " to way index");
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
                        long nodecounter = 0;
                        long minNode = long.MaxValue;
                        long maxNode = long.MinValue;
                        if (pb2.primitivegroup[0].dense != null)
                        {
                            foreach (var n in pb2.primitivegroup[0].dense.id)
                            {
                                nodecounter += n;
                                if (nodecounter < minNode)
                                    minNode = nodecounter;
                                if (nodecounter > maxNode)
                                    maxNode = nodecounter;
                            }
                            nodeFinder2.TryAdd(passedBC, new Tuple<long, long>(minNode, maxNode));
                        }
                    }
                });

                waiting.Add(tasked);
            }
            Task.WaitAll(waiting.ToArray());
            //my logic does require the wayIndex to be in blockID order.
            var sortingwayFinder2 = wayFinder2.OrderBy(w => w.Key).ToList();
            wayFinder2 = new Dictionary<long, long>();
            foreach (var w in sortingwayFinder2)
                wayFinder2.TryAdd(w.Key, w.Value);
            Log.WriteLog("Found " + blockCounter + " blocks. " + relationCounter + " relation blocks and " + wayCounter + " way blocks.");
            foreach (var entry in nodeFinder2.Reverse())
                nodeFinder2Reverse.TryAdd(entry.Key, entry.Value);
            //Lazy optimization: if our node is bigger than roughly the halfway point, search from the end instead of the start.           
            var idx = new Index(nodeFinder2.Count() / 2);
            switchPoint = nodeFinder2.ElementAt(idx).Value.Item2;

            firstWayBlock = wayFinder2.First().Key;
        }

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

        //NOTE: once this is done with the memory stream, it could split off the remaining work to a Task
        //as a possible small multithread optimization. Test this singlethread against the split versions
        //GetBlockBytes (singlethread) and DecodeBlock(taskable)
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

        private byte[] GetBlockBytes(long blockId)
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
            Console.WriteLine("Block " + blockId + " loaded to RAM as bytes");
            return thisblob1;
        }

        private PrimitiveBlock DecodeBlock(byte[] blockBytes)
        {
            var ms2 = new MemoryStream(blockBytes);
            var b2 = Serializer.Deserialize<Blob>(ms2);
            var ms3 = new MemoryStream(b2.zlib_data);
            var dms2 = new ZlibStream(ms3, CompressionMode.Decompress);

            var pulledBlock = Serializer.Deserialize<PrimitiveBlock>(dms2);
            return pulledBlock;
        }

        private OsmSharp.Complete.CompleteRelation GetRelation(long relationId)
        {
            try
            {
                var relationBlockValues = relationFinder[relationId];
                PrimitiveBlock relationBlock = GetBlock(relationBlockValues);

                var relPrimGroup = relationBlock.primitivegroup[0];
                var rel = relPrimGroup.relations.Where(r => r.id == relationId).FirstOrDefault();
                //finally have the core item

                //Should not be relevant with my custom feature interpreter logic.
                //Sanity check - ignore the Labrador Sea until I have a way to process it.
                //if (rel.memids.Count() > 12000) //This is roughly where a stack overflow will reliably occur trying to join rings
                //{
                    //Log.WriteLog("Relation " + rel.id + " too big - ignoring to avoid a stack overflow");
                    //return null;
                //}

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

                //Now get a list of block i know i need now.
                List<long> neededBlocks = new List<long>();
                List<long> wayBlocks = new List<long>();
                //List<long> nodeBlocks = new List<long>();

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
                            //nodeBlocks.Add(FindBlockKeyForNode(idToFind, nodeBlocks));
                            //nodeBlocks = nodeBlocks.Distinct().ToList();
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
                neededBlocks.AddRange(wayBlocks.Distinct());
                //neededBlocks.AddRange(nodeBlocks.Distinct());
                neededBlocks = neededBlocks.Distinct().ToList();
                foreach (var nb in neededBlocks)
                    GetBlock(nb);

                //Ive got all the blocks directly referenced by this relation. Those entries will load the blocks
                //they need later
                OsmSharp.Complete.CompleteRelation r = new OsmSharp.Complete.CompleteRelation();
                r.Id = relationId;
                r.Tags = new OsmSharp.Tags.TagsCollection();

                for (int i = 0; i < rel.keys.Count(); i++)
                {
                    r.Tags.Add(new OsmSharp.Tags.Tag(System.Text.Encoding.UTF8.GetString(relationBlock.stringtable.s[(int)rel.keys[i]]), System.Text.Encoding.UTF8.GetString(relationBlock.stringtable.s[(int)rel.vals[i]])));
                }

                //This makes sure we only load each element once. If a relation references an element more than once (it shouldnt)
                //this saves us from re-creating the same entry.
                //Dictionary<long, OsmSharp.Node> loadedNodes = new Dictionary<long, OsmSharp.Node>();
                Dictionary<long, OsmSharp.Complete.CompleteWay> loadedWays = new Dictionary<long, OsmSharp.Complete.CompleteWay>();
                List<OsmSharp.Complete.CompleteRelationMember> crms = new List<OsmSharp.Complete.CompleteRelationMember>();
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
                            //if (!loadedNodes.ContainsKey(idToFind))
                                //loadedNodes.Add(idToFind, GetNode(idToFind, true, nodeBlocks));
                            //c.Member = loadedNodes[idToFind];
                            break;
                        case Relation.MemberType.WAY:
                            if (!loadedWays.ContainsKey(idToFind))
                                loadedWays.Add(idToFind, GetWay(idToFind, false, wayBlocks));
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

        private OsmSharp.Complete.CompleteWay GetWay(long wayId, bool skipUntagged, List<long> hints = null)
        {
            try
            {
                var wayBlockValues = FindBlockKeyForWay(wayId, hints);

                PrimitiveBlock wayBlock = GetBlock(wayBlockValues);
                var wayPrimGroup = wayBlock.primitivegroup[0];
                var way = wayPrimGroup.ways.Where(w => w.id == wayId).FirstOrDefault();
                if (way == null)
                    return null; //way wasn't in the block it was supposed to be in.
                //finally have the core item

                if (skipUntagged && way.keys.Count == 0)
                    return null;

                //NOTES:
                //This gets all the entries we want from each node, then loads those all in 1 pass per referenced block.
                //This is significantly faster than doing a GetBlock per node when 1 block has mulitple entries
                //its a little complicated but a solid performance boost.
                long idToFind = 0; //more deltas 
                //blockId, nodeID
                List<Tuple<long, long>> nodesPerBlock = new List<Tuple<long, long>>();
                List<long> distinctBlockIds = new List<long>();
                for (int i = 0; i < way.refs.Count; i++)
                {
                    idToFind += way.refs[i];
                    var blockID = FindBlockKeyForNode(idToFind, distinctBlockIds);
                    distinctBlockIds.Add(blockID);
                    distinctBlockIds = distinctBlockIds.Distinct().ToList();
                    nodesPerBlock.Add(Tuple.Create(blockID, idToFind));
                }
                var nodesByBlock = nodesPerBlock.ToLookup(k => k.Item1, v => v.Item2);

                List<OsmSharp.Node> nodeList = new List<OsmSharp.Node>();
                Dictionary<long, OsmSharp.Node> AllNodes = new Dictionary<long, OsmSharp.Node>();
                foreach (var block in nodesByBlock)
                {
                    var someNodes = GetAllNeededNodesInBlock(block.Key, block.Distinct().OrderBy(b => b).ToArray());
                    if (someNodes == null)
                        return null; //throw new Exception("Couldn't load all nodes from a block");
                    foreach (var n in someNodes)
                        AllNodes.Add(n.Key, n.Value);
                }

                //Now I have the data needed to fill in nodes for a way
                OsmSharp.Complete.CompleteWay finalway = new OsmSharp.Complete.CompleteWay();
                finalway.Id = wayId;
                finalway.Tags = new OsmSharp.Tags.TagsCollection();

                //skipUntagged is false from GetRelation, so we can ignore tag data in that case.
                //but we do want tags for when we load a block full of ways. SkipUntagged means we need to read tags to know what we didn't skip.
                if (skipUntagged)
                    for (int i = 0; i < way.keys.Count(); i++)
                    {
                        finalway.Tags.Add(new OsmSharp.Tags.Tag(System.Text.Encoding.UTF8.GetString(wayBlock.stringtable.s[(int)way.keys[i]]), System.Text.Encoding.UTF8.GetString(wayBlock.stringtable.s[(int)way.vals[i]])));
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

        private List<OsmSharp.Node> GetTaggedNodesFromBlock(PrimitiveBlock block)
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

                OsmSharp.Node n = new OsmSharp.Node();
                n.Id = nodeId;
                n.Latitude = DecodeLatLon(lat, block.lat_offset, block.granularity);
                n.Longitude = DecodeLatLon(lon, block.lon_offset, block.granularity);
                n.Tags = tc;
                taggedNodes.Add(n);
            }

            return taggedNodes;
        }

        private OsmSharp.Node GetNode(long nodeId, bool skipTags = true, List<long> hints = null)
        {
            var nodeBlockValues = FindBlockKeyForNode(nodeId, hints);

            PrimitiveBlock nodeBlock = GetBlock(nodeBlockValues);
            var nodePrimGroup = nodeBlock.primitivegroup[0];
            var keyvals = nodePrimGroup.dense.keys_vals;

            //sort out tags ahead of time.
            int entryCounter = 0;
            List<Tuple<int, string, string>> idKeyVal = new List<Tuple<int, string, string>>();
            for (int i = 0; i < keyvals.Count; i++)
            {
                if (keyvals[i] == 0)
                {
                    entryCounter++;
                    continue;
                }
                //skip to next entry.
                idKeyVal.Add(
                    Tuple.Create(entryCounter,
                System.Text.Encoding.UTF8.GetString(nodeBlock.stringtable.s[keyvals[i]]),
                System.Text.Encoding.UTF8.GetString(nodeBlock.stringtable.s[keyvals[i + 1]])
                ));
                i++;
            }

            var decodedTags = idKeyVal.ToLookup(k => k.Item1, v => new OsmSharp.Tags.Tag(v.Item2, v.Item3));

            long nodeCounter = 0;
            int index = -1;
            long latDelta = 0;
            long lonDelta = 0;
            var dense = nodePrimGroup.dense; //this appears to save a little CPU time instead of getting the list each time?
            var denseIds = dense.id;
            var dLat = dense.lat;
            var dLon = dense.lon;
            while (nodeCounter != nodeId)
            {
                index += 1;
                if (index == 8000)
                    return null;
                nodeCounter += denseIds[index];
                latDelta += dLat[index];
                lonDelta += dLon[index];
            }

            OsmSharp.Node filled = new OsmSharp.Node();
            filled.Id = nodeId;
            filled.Latitude = DecodeLatLon(latDelta, nodeBlock.lat_offset, nodeBlock.granularity);
            filled.Longitude = DecodeLatLon(lonDelta, nodeBlock.lon_offset, nodeBlock.granularity);

            if (!skipTags)
            {
                OsmSharp.Tags.TagsCollection tc = new OsmSharp.Tags.TagsCollection();
                foreach (var t in decodedTags[index].ToList())
                    tc.Add(t);

                filled.Tags = tc;
            }
            return filled;
        }

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

        private long FindBlockKeyForNode(long nodeId, List<long> hints = null) //Iterative
        {
            //This is the most-called function in this class, and therefore the most performance-dependent.
            //It also, as written, slows down dramatically the bigger the file gets. 
            //As it turns out, the range of node IDs involved WON'T overlap, so i CAN b-tree search this.

            //Hints is a list of blocks we're already found in the relevant way. Odds are high that
            //any node I need to find is in the same block as another node I've found.
            //This should save a lot of time iterating over the list when I have already found some blocks
            //and shoudn't waste too much time if it isn't in a block already found.
            if (hints != null)
            {
                foreach (var h in hints)
                {
                    var entry = nodeFinder2[h];
                    if (entry.Item1 > nodeId) //this node's minimum is larger than our node, skip
                        continue;

                    if (entry.Item2 < nodeId) //this node's maximum is smaller than our node, skip
                        continue;

                    return h;
                }
            }

            if (nodeId < switchPoint)
                foreach (var nodelist in nodeFinder2)
                {
                    //key is block id
                    //value is the tuple list. 1 is min, 2 is max.
                    if (nodelist.Value.Item1 > nodeId) //this node's minimum is larger than our node, skip
                        continue;

                    if (nodelist.Value.Item2 < nodeId) //this node's maximum is smaller than our node, skip
                        continue;

                    //Actually, we're just gonna return the value here, since we found it, and let it error out later if that node isn't present.
                    //This isn't much of a CPU optimization, but it lets us skip one GetBlock() call.
                    return nodelist.Key;
                }
            else
                foreach (var nodelist in nodeFinder2Reverse)
                {
                    //key is block id
                    //value is the tuple list. 1 is min, 2 is max.
                    //Reverse the check order since we're traversing the opposite direction
                    if (nodelist.Value.Item2 < nodeId) //this node's maximum is smaller than our node, skip
                        continue;
                    if (nodelist.Value.Item1 > nodeId) //this node's minimum is larger than our node, skip
                        continue;

                    //Actually, we're just gonna return the value here, since we found it, and let it error out later if that node isn't present.
                    //This isn't much of a CPU optimization, but it lets us skip one GetBlock() call.
                    return nodelist.Key;
                }

            //couldnt find this node
            throw new Exception("Node Not Found");
        }

        private long FindBlockKeyForWay(long wayId)
        {
            //We cant use hints here as long as this is a single digit index. I need to track min and max to use hints.
            //unlike nodes, ways ARE usually sorted 
            //so we CAN safely just find the block where wayId >= minWay for a block
            //BUT the easiest b-tree logic on a ConcurrentDictionary does more iterating to get indexes than just iterating the list would do.
            foreach (var waylist in wayFinder2)
            {
                //key is block id. value is the max way value in this node. We dont need to check the minimum.
                if (waylist.Value < wayId) //this node's maximum is smaller than our node, skip
                    continue;

                return waylist.Key;
            }

            //couldnt find this way
            throw new Exception("Way Not Found");
        }

        private long FindBlockKeyForWay(long wayId, List<long> hints)
        {
            //We cant use hints here as long as this is a single digit index. I need to track min and max to use hints.
            //unlike nodes, ways ARE usually sorted 
            //so we CAN safely just find the block where wayId >= minWay for a block
            //BUT the easiest b-tree logic on a ConcurrentDictionary does more iterating to get indexes than just iterating the list would do.
            if (hints != null)
                foreach (var h in hints)
                {
                    //we can check this, but we need to look at the previous block too.
                    if (wayFinder2[h] >= wayId && (h == firstWayBlock || wayFinder2[h - 1] < wayId))
                        return h;
                }

            foreach (var waylist in wayFinder2)
            {
                //key is block id. value is the max way value in this node. We dont need to check the minimum.
                if (waylist.Value < wayId) //this node's maximum is smaller than our node, skip
                    continue;

                return waylist.Key;
            }

            //couldnt find this way
            throw new Exception("Way Not Found");
        }

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

        public ConcurrentBag<OsmSharp.Complete.ICompleteOsmGeo> GetGeometryFromBlock(long blockId)
        {
            //This grabs the chosen block, populates everything in it to an OsmSharp.Complete object and returns that list
            try
            {
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();

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
                            foreach(var r in toProcess)
                                relList.Add(Task.Run(() => results.Add(GetRelation(r.id))));

                            Task.WaitAll(relList.ToArray());
                            activeBlocks.Clear(); //Dump them all out of RAM, see how this affects performance. At most a block gets read 3 times more than it would have before.
                        }

                        //original code
                        //foreach (var r in primgroup.relations.OrderByDescending(w => w.memids.Count())) //Ordering should help consistency in runtime, though splitting off the biggest ones to their own thread is a better optimization.
                        //{
                        //    relList.Add(Task.Run(() => results.Add(GetRelation(r.id))));
                        //}
                    }
                    else if (primgroup.ways != null && primgroup.ways.Count() > 0)
                    {
                        //original multithreading logic, similar to relations. Attermpting to do this like nodes is much, much slower and eats tons of RAM
                        foreach (var r in primgroup.ways.OrderByDescending(w => w.refs.Count())) //Ordering should help consistency in runtime, though splitting off the biggest ones to their own thread is a better optimization.
                        {
                            relList.Add(Task.Run(() => results.Add(GetWay(r.id, true))));
                        }
                    }
                    else
                    {
                        //Useful node lists are so small, they lose performance from splitting each step into 1 task per entry.
                        //Inline all that here as one task and return null to skip the rest.
                        writeTasks.Add(Task.Run(() =>
                        {
                            try
                            {
                                var nodes = GetTaggedNodesFromBlock(block);
                                var convertednodes = nodes.Select(n => GeometrySupport.ConvertOsmEntryToStoredElement(n)).ToList();
                                var classForJson = convertednodes.Where(c => c != null).Select(md => new StoredOsmElementForJson(md.id, md.name, md.sourceItemID, md.sourceItemType, md.elementGeometry.AsText(), string.Join("~", md.Tags.Select(t => t.Key + "|" + t.Value)), md.IsGameElement, md.IsUserProvided, md.IsGenerated)).ToList();
                                var textLines = classForJson.Select(c => JsonSerializer.Serialize(c, typeof(StoredOsmElementForJson))).ToList();
                                lock (fileLock)
                                    System.IO.File.AppendAllLines(outputPath + System.IO.Path.GetFileNameWithoutExtension(fi.Name) + ".json", textLines);

                                sw.Stop();
                                Log.WriteLog("block " + blockId + ":" + nodes.Count() + " items out of " + block.primitivegroup[0].dense.id.Count + " created in " + sw.Elapsed);
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

                //Move this logic here to free up RAM by removing blocks once we're done reading data from the hard drive. Should result in fewer errors at the ProcessReaderResults step.
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

                sw.Stop();
                timeList.Add(sw.Elapsed);
                if (results != null && results.Count() != 0)
                Log.WriteLog("block " + blockId + ":" + results.Count() + " items out of " + count + " created in " + sw.Elapsed);
                return results;
            }
            catch (Exception ex)
            {
                Log.WriteLog("error getting geometry: " + ex.Message);
                return null;
            }
        }

        //Taken from OsmSharp (MIT License)
        private static double DecodeLatLon(long valueOffset, long offset, long granularity)
        {
            return .000000001 * (offset + (granularity * valueOffset));
        }
        //end OsmSharp copied functions.

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
            data = new string[wayFinder2.Count()];
            j = 0;
            foreach (var wf in wayFinder2)
            {
                data[j] = wf.Key + ":" + wf.Value;
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
                    wayFinder2.TryAdd(long.Parse(subData2[0]), long.Parse(subData2[1]));
                }

                filename = outputPath + fi.Name + ".nodeIndex";
                data = System.IO.File.ReadAllLines(filename);
                foreach (var line in data)
                {
                    string[] subData2 = line.Split(":");
                    nodeFinder2.TryAdd(long.Parse(subData2[0]), Tuple.Create(long.Parse(subData2[1]), long.Parse(subData2[2])));
                }

                //I never use NodeFinder2 with the key, its always iterated over. It should be a list or a sorted concurrent entry
                foreach (var entry in nodeFinder2.Reverse())
                    nodeFinder2Reverse.TryAdd(entry.Key, entry.Value);

                //Lazy optimization: if our node is bigger than roughly the halfway point, search from the end instead of the start.           
                var idx = new Index(nodeFinder2.Count() / 2);
                switchPoint = nodeFinder2.ElementAt(idx).Value.Item2;
                firstWayBlock = wayFinder2.First().Key;
            }
            catch (Exception ex)
            {
                return;
            }
        }

        private void SaveCurrentBlock(long blockID)
        {
            string filename = outputPath + fi.Name + ".progress";
            System.IO.File.WriteAllText(filename, blockID.ToString());
        }

        private long FindLastCompletedBlock()
        {
            string filename = outputPath + fi.Name + ".progress";
            long blockID = long.Parse(System.IO.File.ReadAllText(filename));
            return blockID;
        }

        private void CleanupFiles()
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

        public void ShowWaitInfo()
        {
            Task.Run(() =>
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
                        //TimeSpan ts = new TimeSpan((long)timeList.Average(t => t.Ticks) * nextBlockId);
                        //Log.WriteLog("Estimated time remaining: " + ts.ToString()); //This will be high, since we start with the slowest blocks and move to the fastest ones.
                    }
                    System.Threading.Thread.Sleep(60000);
                }
            });
        }

        public Task ProcessReaderResults(IEnumerable<OsmSharp.Complete.ICompleteOsmGeo> items, string saveFilename, bool saveToDb = false)
        {
            //This one is easy, we just dump the geodata to the file.
            ConcurrentBag<StoredOsmElement> elements = new ConcurrentBag<StoredOsmElement>();
            DateTime startedProcess = DateTime.Now;

            if (items == null || items.Count() == 0)
                return null;

            relList = new ConcurrentBag<Task>();
            foreach (var r in items)
            {
                if (r != null)
                    relList.Add(Task.Run(() => elements.Add(GeometrySupport.ConvertOsmEntryToStoredElement(r))));
            }
            Task.WaitAll(relList.ToArray());
            relList = new ConcurrentBag<Task>();

            if (saveToDb)
            {
                var db = new PraxisContext();
                db.StoredOsmElements.AddRange(elements);
                db.SaveChanges();
                return null;
            }
            else
            {
                ConcurrentBag<string> results = new ConcurrentBag<string>();
                foreach (var md in elements.Where(e => e != null))
                {
                    relList.Add(Task.Run(() =>
                    {
                        var recordVersion = new StoredOsmElementForJson(md.id, md.name, md.sourceItemID, md.sourceItemType, md.elementGeometry.AsText(), string.Join("~", md.Tags.Select(t => t.Key + "|" + t.Value)), md.IsGameElement, md.IsUserProvided, md.IsGenerated);
                        var test = JsonSerializer.Serialize(recordVersion, typeof(StoredOsmElementForJson));
                        results.Add(test);
                    }));
                }
                Task.WaitAll(relList.ToArray());

                var monitorTask = System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        lock (fileLock)
                        {
                            System.IO.File.AppendAllLines(saveFilename, results);
                        }
                        Log.WriteLog("Data written to disk");
                    }
                    catch (Exception ex)
                    {
                        Log.WriteLog("Error writing data to disk:" + ex.Message);
                    }
                });

                return monitorTask;
            }
        }

        public void BenchmarkCheck()
        {
            //load Ohio, see how long it takes to look up all ways


        }
    }
}
