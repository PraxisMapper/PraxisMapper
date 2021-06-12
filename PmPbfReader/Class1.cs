using System;
using System.IO;
using System.Collections.Generic;
using Google.Protobuf;
using ProtoBuf.Meta;
using ProtoBuf;
using Ionic.Zlib;
using System.Linq;
using System.Collections.Concurrent;
using System.Threading.Tasks;
//one function copied from OsmSharp.IO.Pbf.Encoder

namespace PmPbfReader
{
    //TODO: keep activeBlocks in memory when possible, and dump it under memory pressure?
    //I should do a run where I could how many times each block is loaded from disk and report that
    //Might be interesting to see if the average block is loaded more than once.

    //I need a way to track which blocks are done, so i can resume this later for big files.

    //rough usage plan:
    //Open a file - locks access
    //IndexFileParallel - can now search the file. required call. Fills stuff that will persist in memory, so this is the limit on filesize now.
    //SaveBlockInfo if you want to use LoadBlockInfo later
    //loop GetGeometryFromBlock from BlockCount() to 1 - create everything from the given block as OSMSharp.CompleteGeo. Could write that to its own file and be able to resume later.
    //SaveCurrentBlock after each GetGEometrFromBlock for resuming.
    //LoadindexData
    //ResumeFromLastCompletedBlock


    //For the most part, I want to grab all this stuff
    //Though i can skip untagged elements to save some time.

    //So far
    //Delaware stats:
    //index: 0:03
    //index-parallel: 0:02
    //index-blocks: practically 0
    //load:0:01:20
    //load-parallel: 0:00:65
    //lastblock: 3:11
    //Ohio stats
    //index: 5:42
    //index-parallel: 2:04 ==> 6 seconds
    //index-blocks: 0:00:60
    //load: :26
    //load-parallel: :11
    // lastblock: 8:02 => 1:15
    //both states gets over 50% faster with parallel logic.
    //once i have the file dumped to RAM, i could work backwards and index stuff
    //and possibly remove blocks once they don't reference things anymore.

    //getlastblock perf history:
    //1: 24 minutes, 2780 / 3008 (parallel getRelations)
    //2: 13 minutes, 2780 / 3008 (GetNode uses activeBlocks)
    //3: 1:22, 2780 / 3008 (FindBlockKeyForNode uses activeBlocks) //still 253 blocks to process, and i need a way to scan Nodes for stuff worth tracking.
    //4: 0:27, 2780 / 3008 (GetRelation now uses ActiveBlocks for the relation's block) //estimates 2 hours still? 30 seconds and 253 blocks
    //5: 0:26, 2780 / 3008(no console log for relation processing)
    //6: 0:33, 2780 / 3308 (inflate nodes to smallnodes on first lookup) -might be faster to only process needed nodes on demand. How about that.
    //7: 0:24, 2779 / 3308 (undid previous changes, small cleanup, VS reboot)
    //8: 0:30, 2778 / 3308 (load all nodes per block instead of individually)
    //9: 0:28, 2778 /3308  (skip lookup for primGroup[0] in node searches)

    //ohio, starting at 7:
    //7: 1:30 relation block, way block 10.5 seconds.
    //9: 1:12 relation block, way block 51 seconds
    //10 1:24 relation [but 1GB less RAM] (skip relations without inner/outer members)
    //11 , 41 seconds ways (skip serializing some data in protobufnet)
    //This is approaching usable.

    //holy crap, this takes 1-3 seconds per way block in Release mode.
    //the debug support is eating SO MUCH TIME on this. Amazing.
    //Doing the Ohio file on my tablet takes ~14 minutes. I think Larry does it in 9 on my tower.
    //Tower doing this process now, end of day 6/10, release mode:  12:23:20 to 12:33:54. 10 minutes, 30 seconds.  90 extra seconds slower, (possibly) missing only 44MB of entries out of 1.2GB (96% accurate to the old version)
    //I am extremely happy with this performance.
    //12:30 on surface

    //Checking, access multiple times to the same block is infrequent (through relation and ways, the most-accesed blocks get pulled in
    //about 20% of the time). The simple heuristic for this is to save the blocks from the previous run in memory, since
    //when a block is accessed multiple times its usually from an adjacent block. Is it worth the effort to pick and choose what
    //entries in activeBlocks get dropped or not dropped to avoid the disk access/serializer?

    //This should work with OsmSharp objects, to avoid rewriting the rest of my app.

    //This should now pretty much use all the CPU threads as much as possible now. The only single-threaded part is 
    //actually writing to the destination file. I should kick that to a Task as well.
    //And do less console logging. Once i hit the node-only blocks, it practically single-threads blocking on the console info.
    public class PbfReader
    {
        FileInfo fi;
        FileStream fs;

        //these are osmId, <blockId, primitiveGroupID>
        //but primGroupID is always 0, so i should switch this around to just blockID
        ConcurrentDictionary<long, Tuple<long, int>> relationFinder = new ConcurrentDictionary<long, Tuple<long, int>>();

        //this is blockId, <minNode, maxNode>.
        ConcurrentDictionary<long, Tuple<long, long>> nodeFinder2 = new ConcurrentDictionary<long, Tuple<long, long>>();
        //blockId, maxWayId since ways are sorted in order.
        ConcurrentDictionary<long, long> wayFinder2 = new ConcurrentDictionary<long, long>();

        Dictionary<long, long> blockPositions = new Dictionary<long, long>();
        Dictionary<long, int> blockSizes = new Dictionary<long, int>();

        private PrimitiveBlock _block = new PrimitiveBlock();
        private BlobHeader _header = new BlobHeader();

        //Moving this here, so I can stop requiring it as a parameter.
        ConcurrentDictionary<long, PrimitiveBlock> activeBlocks = new ConcurrentDictionary<long, PrimitiveBlock>();

        //I will use the write lock to make sure threads don't read the wrong data
        //the names will be misleading, since i dont want to use overlapping IO on these even though
        //the docs say I could, since I'd need to Seek() to a position and then read and its possible
        //threads would change the Seek point before the ReadAsync was called.
        System.Threading.ReaderWriterLockSlim msLock = new System.Threading.ReaderWriterLockSlim();

        //List<Tuple<long, long>> accessCounter = new List<Tuple<long, long>>();
        //List<long> accessedBlocks = new List<long>();
        Dictionary<long, bool> accessedBlocks = new Dictionary<long, bool>();

        public long BlockCount()
        {
            return blockPositions.Count();
        }

        public void Open(string filename)
        {
            fi = new FileInfo(filename);
            Console.WriteLine(fi.FullName + " | " + fi.Length);
            fs = File.OpenRead(filename);

            //Not certain if this improves performance later or not.
            Serializer.PrepareSerializer<PrimitiveBlock>();
            Serializer.PrepareSerializer<Blob>();
        }

        public void Close()
        {
            fs.Close();
            fs.Dispose();
        }

        public void ProcessFile(string filename)
        {
            Open(filename);
            LoadBlockInfo();
            long nextBlockId = 0;
            if (relationFinder.Count == 0)
            {
                IndexFileParallel();
                SaveBlockInfo();
                nextBlockId = BlockCount() - 1;
                SaveCurrentBlock(BlockCount());
            }
            else
            {
                nextBlockId = FindLastCompletedBlock() - 1;
            }

            for (var block = nextBlockId; block > 0; block--)
            {
                long thisBlockId = block;
                var geoData = GetGeometryFromBlock(thisBlockId);
                //There are large relation blocks where you can see how much time is spent writing them or waiting for one entry to
                //process as the apps drops to a single thread in use, but I can't do much about those if I want to be able to resume a process.
                if (geoData != null) //This process function is sufficiently parallel that I don't want to throw it off to a Task. The only sequential part is writing the data to the file, and I need that to keep accurate track of which blocks have beeen written to the file.
                        CoreComponents.PbfFileParser.ProcessPMPBFResults(geoData, System.IO.Path.GetFileNameWithoutExtension(filename) + ".json");            
                SaveCurrentBlock(block);
            }

            CleanupFiles();
        }

        public void IndexFileParallel()
        {
            fs.Position = 0;
            long blockCounter = 0;
            blockPositions = new Dictionary<long, long>();
            blockSizes = new Dictionary<long, int>();
            relationFinder = new ConcurrentDictionary<long, Tuple<long, int>>();
            nodeFinder2 = new ConcurrentDictionary<long, Tuple<long, long>>();
            wayFinder2 = new ConcurrentDictionary<long, long>();

            BlobHeader bh = new BlobHeader();
            Blob b = new Blob();

            HeaderBlock hb = new HeaderBlock();
            PrimitiveBlock pb = new PrimitiveBlock();

            //Only one OsmHeader, at the start
            Serializer.MergeWithLengthPrefix(fs, bh, PrefixStyle.Fixed32BigEndian);
            hb = Serializer.Deserialize<HeaderBlock>(fs, length: bh.datasize); //only one of these per file    
            //Console.WriteLine(hb.source + "|" + hb.writingprogram);
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
                    if (pb2.primitivegroup.Count() > 1)
                        Console.WriteLine("This block has " + pb2.primitivegroup.Count() + " groups!");

                    var group = pb2.primitivegroup[0];
                    if (group.ways.Count > 0)
                        wayCounter++;
                    if (group.relations.Count > 0)
                        relationCounter++;

                    foreach (var r in pb2.primitivegroup[0].relations)
                    {
                        relationFinder.TryAdd(r.id, new Tuple<long, int>(passedBC, 0));
                    }

                    if (pb2.primitivegroup[0].ways.Count > 0)
                    {
                        var wMax = pb2.primitivegroup[0].ways.Max(w => w.id);
                        wayFinder2.TryAdd(passedBC, wMax);
                    }

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
                });

                waiting.Add(tasked);
            }
            Task.WaitAll(waiting.ToArray());
            Console.WriteLine("Found " + blockCounter + " blocks. " + relationCounter + " relation blocks and " + wayCounter + " way blocks.");
        }

        public void IndexFileBlocks()
        {
            //Only fills in blockPositions and blockSizes
            //Most files seems to be sorted with the Relations at the end
            //then Ways before those and node only entries last.
            //So i want to see if blocks are self-contained fully, and 
            //read relations first, then ways, then skim nodes for data.

            //This should also dump the block list to a file, so it can be serialized back later to save time.
            fs.Position = 0;
            long blockCounter = 0;
            blockPositions = new Dictionary<long, long>();
            blockSizes = new Dictionary<long, int>();

            BlobHeader bh = new BlobHeader();
            Blob b = new Blob();

            HeaderBlock hb = new HeaderBlock();
            PrimitiveBlock pb = new PrimitiveBlock();

            //Only one OsmHeader, at the start
            bh = Serializer.DeserializeWithLengthPrefix<BlobHeader>(fs, PrefixStyle.Fixed32BigEndian);
            hb = Serializer.Deserialize<HeaderBlock>(fs, length: bh.datasize); //only one of these per file    
            Console.WriteLine(hb.source + "|" + hb.writingprogram);
            blockPositions.Add(0, fs.Position);
            blockSizes.Add(0, bh.datasize);

            //Data blocks start at 1
            while (fs.Position != fs.Length)
            {
                blockCounter++;
                bh = Serializer.DeserializeWithLengthPrefix<BlobHeader>(fs, PrefixStyle.Fixed32BigEndian);
                blockPositions.Add(blockCounter, fs.Position);
                blockSizes.Add(blockCounter, bh.datasize);
                fs.Seek(bh.datasize, SeekOrigin.Current);
            }
        }

        public PrimitiveBlock GetBlock(long blockId)
        {
            //If the block is in memory, return it.
            //If not, load it and return it.
            accessedBlocks.TryAdd(blockId, true);
            if (!activeBlocks.ContainsKey(blockId))
                activeBlocks.TryAdd(blockId, GetBlockFromFile(blockId));

            return activeBlocks[blockId];
        }

        //NOTE: once this is done with the memory stream, it could split off the remaining work to a Task
        //as a possible small multithread optimization. Test this singlethread against the split versions
        //GetBlockBytes (singlethread) and DecodeBlock(taskable)
        public PrimitiveBlock GetBlockFromFile(long blockId)
        {
            msLock.EnterWriteLock();
            long pos1 = blockPositions[blockId];
            int size1 = blockSizes[blockId];
            fs.Seek(pos1, SeekOrigin.Begin);
            byte[] thisblob1 = new byte[size1];
            fs.Read(thisblob1, 0, size1);
            msLock.ExitWriteLock();

            //fs.ReadAsync(thisblob1, (int)pos1, size1);
            //NOTE: ReadAsync takes an int, not a long, so it won't work for big files.
            var ms2 = new MemoryStream(thisblob1);
            var b2 = Serializer.Deserialize<Blob>(ms2);
            var ms3 = new MemoryStream(b2.zlib_data);
            var dms2 = new ZlibStream(ms3, CompressionMode.Decompress);

            var pulledBlock = Serializer.Deserialize<PrimitiveBlock>(dms2);
            return pulledBlock;
        }

        public byte[] GetBlockBytes(long blockId)
        {
            msLock.EnterWriteLock();
            long pos1 = blockPositions[blockId];
            int size1 = blockSizes[blockId];
            fs.Seek(pos1, SeekOrigin.Begin);
            byte[] thisblob1 = new byte[size1];
            fs.Read(thisblob1, 0, size1);
            msLock.ExitWriteLock();
            Console.WriteLine("Block " + blockId + " loaded to RAM as bytes");
            return thisblob1;
        }

        public PrimitiveBlock DecodeBlock(byte[] blockBytes)
        {
            var ms2 = new MemoryStream(blockBytes);
            var b2 = Serializer.Deserialize<Blob>(ms2);
            var ms3 = new MemoryStream(b2.zlib_data);
            var dms2 = new ZlibStream(ms3, CompressionMode.Decompress);

            var pulledBlock = Serializer.Deserialize<PrimitiveBlock>(dms2);
            return pulledBlock;
        }

        public OsmSharp.Complete.CompleteRelation GetRelation(long relationId)
        {
            try
            {
                //Console.WriteLine("getting relation " + relationId);
                //Run after indexing file
                //load only relevant blocks for this entry
                var relationBlockValues = relationFinder[relationId];
                PrimitiveBlock relationBlock = GetBlock(relationBlockValues.Item1);

                var relPrimGroup = relationBlock.primitivegroup[0];
                var rel = relPrimGroup.relations.Where(r => r.id == relationId).FirstOrDefault();
                //finally have the core item

                //sanity check - if this relation doesn't have inner or outer role members,
                //its not one i can process.
                foreach (var role in rel.roles_sid)
                {
                    string roleType = System.Text.Encoding.UTF8.GetString(relationBlock.stringtable.s[role]);
                    if (roleType == "inner" || roleType == "outer")
                        break;

                    return null; //This relation had no useful members
                }

                if (rel.keys.Count == 0) //I cant use untagged areas for anything.
                    return null;

                //NOTE: this isn't faster if I do the same setup for Ways that i do
                //in GetWAys for Nodes. There's much less looping/iterating on ways, since they're stored by ID
                //instead of dense-packed. 
                //i don't think most relations use the same way more than once, so this might be unnecessay

                //Now get a list of block i know i need now.
                List<long> neededBlocks = new List<long>();

                //memIds is delta-encoded. Gotta do the counter thing.
                long idToFind = 0;
                for (int i = 0; i < rel.memids.Count; i++)
                {
                    idToFind += rel.memids[i];
                    Relation.MemberType typeToFind = rel.types[i];

                    switch (typeToFind)
                    {
                        case Relation.MemberType.NODE:
                            neededBlocks.Add(FindBlockKeyForNode(idToFind));
                            break;
                        case Relation.MemberType.WAY:
                            neededBlocks.Add(FindBlockKeyForWay(idToFind));
                            break;
                        case Relation.MemberType.RELATION: //TODO: should probably ignore meta-relations
                                                           //neededBlocks.Add(relationFinder[idToFind].Item1);
                            break;
                    }
                }

                neededBlocks = neededBlocks.Distinct().ToList(); //I'll also need to fill in any entries from 
                foreach (var nb in neededBlocks)
                {
                    if (!activeBlocks.ContainsKey(nb))
                    {
                        activeBlocks.TryAdd(nb, GetBlockFromFile(nb));
                        //Console.WriteLine("Block " + nb + " loaded to RAM");
                    }
                }

                //Ive got all the blocks directly referenced by this relation. But i need to do at least one more pass
                //because Ways may or may not need new blocks too.
                OsmSharp.Complete.CompleteRelation r = new OsmSharp.Complete.CompleteRelation();
                r.Id = relationId;
                r.Tags = new OsmSharp.Tags.TagsCollection();

                for (int i = 0; i < rel.keys.Count(); i++)
                {
                    r.Tags.Add(new OsmSharp.Tags.Tag(System.Text.Encoding.UTF8.GetString(relationBlock.stringtable.s[(int)rel.keys[i]]), System.Text.Encoding.UTF8.GetString(relationBlock.stringtable.s[(int)rel.vals[i]])));
                }

                Dictionary<long, OsmSharp.Node> loadedNodes = new Dictionary<long, OsmSharp.Node>();
                Dictionary<long, OsmSharp.Complete.CompleteWay> loadedWays = new Dictionary<long, OsmSharp.Complete.CompleteWay>();

                idToFind = 0;
                for (int i = 0; i < rel.memids.Count; i++)
                {
                    idToFind += rel.memids[i];
                    Relation.MemberType typeToFind = rel.types[i];
                    switch (typeToFind)
                    {
                        case Relation.MemberType.NODE:
                            if (!loadedNodes.ContainsKey(idToFind))
                                loadedNodes.Add(idToFind, GetNode(idToFind, true));
                            break;
                        case Relation.MemberType.WAY:
                            if (!loadedWays.ContainsKey(idToFind))
                                loadedWays.Add(idToFind, GetWay(idToFind, false));
                            break;
                    }

                }

                //final pass, to make sure elements are in the correct order
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
                        //these need to be inner/outer, not Node/Way
                        case Relation.MemberType.NODE:
                            c.Member = loadedNodes[idToFind];
                            break;
                        case Relation.MemberType.WAY:
                            c.Member = loadedWays[idToFind];
                            break;
                    }
                    crms.Add(c);
                }
                r.Members = crms.ToArray();
                return r;
            }
            catch (Exception ex)
            {
                Console.WriteLine("relation failed:" + ex.Message);
                return null;
            }
        }

        //simple version
        //Doesn't do some of the smart stuff to only iterate over each block once, but it's slower for doing so.
        public OsmSharp.Complete.CompleteWay GetWaySimple(long wayId, bool skipUntagged)
        {
            try
            {
                //Console.WriteLine("getting way " + wayId);
                //Run after indexing file
                //load only relevant blocks for this entry
                //var wayBlockValues = wayFinder[wayId];
                var wayBlockValues = FindBlockKeyForWay(wayId);

                PrimitiveBlock wayBlock = GetBlock(wayBlockValues);
                var wayPrimGroup = wayBlock.primitivegroup[0];
                var way = wayPrimGroup.ways.Where(w => w.id == wayId).FirstOrDefault();
                if (way == null)
                    return null; //way wasn't in the block it was supposed to be in.
                //finally have the core item

                if (skipUntagged && way.keys.Count == 0)
                    return null;


                //NOTES:
                //This commented out code seemed to run a lot faster.
                //BUT the more straight-forward setup I use generated a lot fewer errors. Almost all ways get translated in a block that way
                //versus about half. Maybe I missed something on this logic? If I could get each block back to 1-2 seconds instead of 5 with 95+% accuracy, I'd be very happy.
                //long idToFind = 0; //more deltas 
                //List<long> neededBlocks = new List<long>();
                //blockId, nodeID
                //List<Tuple<long, long>> nodesPerBlock = new List<Tuple<long, long>>();

                //for (int i = 0; i < way.refs.Count; i++)
                //{
                //idToFind += way.refs[i];
                //var blockID = FindBlockKeyForNode(idToFind);
                //neededBlocks.Add(blockID);
                //nodesPerBlock.Add(Tuple.Create(blockID, idToFind));
                //}

                //neededBlocks = neededBlocks.Distinct().ToList();
                //var nodesByBlock = nodesPerBlock.ToLookup(k => k.Item1, v => v.Item2);

                //foreach (var nb in neededBlocks)
                //{
                //    if (!activeBlocks.ContainsKey(nb))
                //    {
                //        activeBlocks.TryAdd(nb, GetBlockFromFile(nb));
                //    }
                //}
                //Now I have the data needed to fill in nodes for a way
                OsmSharp.Complete.CompleteWay finalway = new OsmSharp.Complete.CompleteWay();
                finalway.Id = wayId;
                finalway.Tags = new OsmSharp.Tags.TagsCollection();

                //skipUntagged is false from GetRelation, so we can ignore tag data in that case as well.
                //but we do want tags for when we load a block full of ways.
                if (skipUntagged)
                    for (int i = 0; i < way.keys.Count(); i++)
                    {
                        finalway.Tags.Add(new OsmSharp.Tags.Tag(System.Text.Encoding.UTF8.GetString(wayBlock.stringtable.s[(int)way.keys[i]]), System.Text.Encoding.UTF8.GetString(wayBlock.stringtable.s[(int)way.vals[i]])));
                    }

                //new plan
                List<OsmSharp.Node> nodeList = new List<OsmSharp.Node>();
                Dictionary<long, OsmSharp.Node> AllNodes = new Dictionary<long, OsmSharp.Node>();
                //foreach (var block in nodesByBlock)
                //{
                    //var someNodes = GetAllNeededNodesInBlock(block.Key, block.ToList());
                    //if (someNodes == null)
                        //throw new Exception("Couldn't load all nodes from a block");
                    //foreach (var n in someNodes)
                        //AllNodes.Add(n.Key, n.Value);
                //}

                long idToFind = 0;
                foreach (var node in way.refs)
                {
                    idToFind += node; //delta coding.
                    //blockID = FindBlockKeyForNode(idToFind);
                    var osmnode = GetNode(idToFind);
                    if (osmnode == null)
                        throw new Exception("couldn't load all nodes for a way!");
                    nodeList.Add(osmnode);
                }
                finalway.Nodes = nodeList.ToArray();

                //Console.WriteLine("got way " + wayId);
                return finalway;
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetWay failed: " + ex.Message);
                return null; //Failed to get way, probably because a node didn't exist in the file.
            }
        }

        //Complex version
        public OsmSharp.Complete.CompleteWay GetWay(long wayId, bool skipUntagged)
        {
            try
            {
                //Console.WriteLine("getting way " + wayId);
                //Run after indexing file
                //load only relevant blocks for this entry
                //var wayBlockValues = wayFinder[wayId];
                var wayBlockValues = FindBlockKeyForWay(wayId);

                PrimitiveBlock wayBlock = GetBlock(wayBlockValues);
                var wayPrimGroup = wayBlock.primitivegroup[0];
                var way = wayPrimGroup.ways.Where(w => w.id == wayId).FirstOrDefault();
                if (way == null)
                    return null; //way wasn't in the block it was supposed to be in.
                //finally have the core item

                if (skipUntagged && way.keys.Count == 0)
                    return null;


                //NOTES:
                //This is significantly faster than doing a GetBlock per node when 1 block has mulitple entries
                //its a little complicated but a solid performance boost. Copying this logic to GetRelation for ways.
                long idToFind = 0; //more deltas 
                //blockId, nodeID
                List<Tuple<long, long>> nodesPerBlock = new List<Tuple<long, long>>();

                for (int i = 0; i < way.refs.Count; i++)
                {
                    idToFind += way.refs[i];
                    var blockID = FindBlockKeyForNode(idToFind);
                    nodesPerBlock.Add(Tuple.Create(blockID, idToFind));
                }
                var nodesByBlock = nodesPerBlock.ToLookup(k => k.Item1, v => v.Item2);

                List<OsmSharp.Node> nodeList = new List<OsmSharp.Node>();
                Dictionary<long, OsmSharp.Node> AllNodes = new Dictionary<long, OsmSharp.Node>();
                foreach (var block in nodesByBlock)
                {
                    var someNodes = GetAllNeededNodesInBlock(block.Key, block.Distinct().ToList());
                    if (someNodes == null)
                        throw new Exception("Couldn't load all nodes from a block");
                    foreach (var n in someNodes)
                        AllNodes.Add(n.Key, n.Value);
                }

                //Now I have the data needed to fill in nodes for a way
                OsmSharp.Complete.CompleteWay finalway = new OsmSharp.Complete.CompleteWay();
                finalway.Id = wayId;
                finalway.Tags = new OsmSharp.Tags.TagsCollection();

                //skipUntagged is false from GetRelation, so we can ignore tag data in that case as well.
                //but we do want tags for when we load a block full of ways.
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

                //Console.WriteLine("got way " + wayId);
                return finalway;
            }
            catch (Exception ex)
            {
                Console.WriteLine("GetWay failed: " + ex.Message + ex.StackTrace);
                //Console.ReadLine();
                return null; //Failed to get way, probably because a node didn't exist in the file.
            }
        }
        
        public List<OsmSharp.Node> GetTaggedNodesFromBlock(PrimitiveBlock block)
        {
            // try
            //{
            List<OsmSharp.Node> taggedNodes = new List<OsmSharp.Node>(8000);
            var dense = block.primitivegroup[0].dense;

            //Shortcut: if dense.keys.count == 0, there's no tagged nodes at all here
            if (dense.keys_vals.Count == 8000)
                return taggedNodes;

            //sort out tags ahead of time.
            int entryCounter = 0;
            //Dictionary<int, List<OsmSharp.Tags.Tag>> decodedTags = new Dictionary<int, List<OsmSharp.Tags.Tag>>();
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
            //}
            //catch(Exception ex)
            //{
            //  Console.WriteLine("Error loading nodes: " + ex.Message);
            // return null;
            //}
        }

        public OsmSharp.Node GetNode(long nodeId, bool skipTags = true)
        {
            //Console.WriteLine("getting node " + nodeId);
            //Run after indexing file
            //load only relevant blocks for this entry
            var nodeBlockValues = FindBlockKeyForNode(nodeId);

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
            //Console.WriteLine("got node " + nodeId);
            return filled;
        }

        public Dictionary<long, OsmSharp.Node> GetAllNeededNodesInBlock(long blockId, List<long> nodeIds)
        {
            //GetWay() has already ensured that activeBlocks contains the data I need, so i can skip checking it.
            Dictionary<long, OsmSharp.Node> results = new Dictionary<long, OsmSharp.Node>();

            var block = GetBlock(blockId);
            var group = block.primitivegroup[0];

            int index = -1;
            long nodeCounter = 0;
            long latDelta = 0;
            long lonDelta = 0;
            var denseIds = group.dense.id;
            var dLat = group.dense.lat;
            var dLon = group.dense.lon;
            while (results.Count < nodeIds.Count())
            {
                index++;
                if (index == 8000)
                    throw new Exception("Node not found in indexed node!");
                    //This node was't found.

                nodeCounter += denseIds[index];
                latDelta += dLat[index];
                lonDelta += dLon[index];

                if (nodeIds.Contains(nodeCounter))
                {
                    OsmSharp.Node filled = new OsmSharp.Node();
                    filled.Id = nodeCounter;
                    filled.Latitude = DecodeLatLon(latDelta, block.lat_offset, block.granularity);
                    filled.Longitude = DecodeLatLon(lonDelta, block.lon_offset, block.granularity);
                    results.Add(nodeCounter, filled);
                }
            }
            return results;
        }

        
        public long FindBlockKeyForNode(long nodeId)
        {
            foreach (var nodelist in nodeFinder2)
            {
                //key is block id
                //value is the tuple list. 1 is min, 2 is max.
                if (nodelist.Value.Item1 > nodeId) //this node's minimum is larger than our node, skip
                    continue;

                if (nodelist.Value.Item2 < nodeId) //this node's maximum is smaller than our node, skip
                    continue;

                var nodeBlock = GetBlock(nodelist.Key);
                var group = nodeBlock.primitivegroup[0];
                var denseIds = group.dense.id;

                long nodecounter = 0;
                int nodeIndex = -1;
                //as much as i want to tree search this, the negative delta values really mess that up, since there;s
                //no guarentee nodes are sorted by id
                while (nodeIndex < 7999) //groups can only have 8000 entries.
                {
                    nodeIndex++;
                    nodecounter += denseIds[nodeIndex];
                    if (nodecounter == nodeId)
                        return nodelist.Key;
                }
            }

            //couldnt find this node
            throw new Exception("Node Not Found");
        }

        public long FindBlockKeyForWay(long wayId)
        {
            //unlike nodes, ways ARE usually sorted 
            //so we CAN safely just find the block where wayId >= minWay for a block.
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

        public List<OsmSharp.Complete.ICompleteOsmGeo> GetGeometryFromBlock(long blockId)
        {
            //This grabs the chosen block, populates everything in it to an OsmSharp.Complete object
            //and returns that list. Removes the block from memory once that's done.
            //This is the 'main' function I think i'll use to do most of the work originally.
            //I want this to be self-contained RAM wise, so that everything this block references eventually
            //gets pulled in, and can be dropped from memory when this function ends.
            try
            {
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();

                //oriignal, easiest logic. Clear out ram entirely and redo it.
                //activeBlocks = new ConcurrentDictionary<long, PrimitiveBlock>(8, (int)blockPositions.Keys.Max()); //Clear out existing memory each block.
                
                //Slightly more complex: only remove blocks we didn't access last call. saves some serialization effort
                //will still remove 80% of blocks each call. Is this faster than reloading each thing?
                foreach(var blockRead in activeBlocks)
                {
                    if (!accessedBlocks.ContainsKey(blockRead.Key))
                        activeBlocks.TryRemove(blockRead);
                }
                accessedBlocks.Clear();

                
                var block = GetBlock(blockId);
                List<OsmSharp.Complete.ICompleteOsmGeo> results = new List<OsmSharp.Complete.ICompleteOsmGeo>();

                foreach (var primgroup in block.primitivegroup)
                {
                    if (primgroup.relations != null && primgroup.relations.Count() > 0)
                    {
                        //foreach (var r in primgroup.relations)
                        Parallel.ForEach(primgroup.relations, r =>
                        {
                            var relation = GetRelation(r.id);
                            if (relation != null)
                                //continue;
                                results.Add(relation);
                        });
                    }
                    else if (primgroup.ways != null && primgroup.ways.Count() > 0)
                    {
                        //foreach (var w in primgroup.ways)
                        Parallel.ForEach(primgroup.ways, w =>
                        {
                            var way = GetWay(w.id, true); //here, I skip untagged geometry.
                            if (way != null)
                                results.Add(way);
                        });
                    }
                    else
                    {
                        var nodes = GetTaggedNodesFromBlock(block);
                        results.AddRange(nodes);
                    }
                }
                var count = (block.primitivegroup[0].relations?.Count > 0 ? block.primitivegroup[0].relations.Count :
                    block.primitivegroup[0].ways?.Count > 0 ? block.primitivegroup[0].ways.Count :
                    block.primitivegroup[0].dense.id.Count);

                sw.Stop();
                Console.WriteLine("block " + blockId + ":" + results.Count() + " items out of " + count + " created without errors in " + sw.Elapsed);

                //ListMostAccessedBlocks();

                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine("error getting geometry: " + ex.Message);
                return null;
            }

        }

        //Looks like this slows things down. Keeping for reference.
        //public Dictionary<long, SmallNode> InflateNodes(PrimitiveBlock rawBlock)
        //{
        //    if (rawBlock.primitivegroup[0].dense == null)
        //        return new Dictionary<long, SmallNode>();

        //    Dictionary<long, SmallNode> results = new Dictionary<long, SmallNode>(8000);

        //    long idCounter = 0;
        //    int idIndex = -1;
        //    long tagIndex  = 0;
        //    foreach(var n in rawBlock.primitivegroup[0].dense.id)
        //    {
        //        idIndex++;
        //        idCounter += n;
        //        var lat = DecodeLatLon(rawBlock.primitivegroup[0].dense.lat[idIndex], rawBlock.lat_offset, rawBlock.granularity);
        //        var lon = DecodeLatLon(rawBlock.primitivegroup[0].dense.lon[idIndex], rawBlock.lon_offset, rawBlock.granularity);
        //        results.Add(idCounter, new SmallNode(idCounter, lat, lon));
        //    }

        //    return results;

        //}

        //Taken from OsmSharp
        public static double DecodeLatLon(long valueOffset, long offset, long granularity)
        {
            return .000000001 * (offset + (granularity * valueOffset));
        }

        public void SaveBlockInfo()
        {
            string filename = fi.Name + ".blockinfo";
            //now deserialize
            string[] data = new string[blockPositions.Count()];
            for (int i = 0; i < data.Count(); i++)
            {
                data[i] = i + ":" + blockPositions[i] + ":" + blockSizes[i];
            }
            System.IO.File.WriteAllLines(filename, data);

            filename = fi.Name + ".relationIndex";
            data = new string[relationFinder.Count()];
            int j = 0;
            foreach (var wf in relationFinder)
            {
                data[j] = wf.Key + ":" + wf.Value.Item1 + ":" + wf.Value.Item2;
                j++;
            }
            System.IO.File.WriteAllLines(filename, data);

            filename = fi.Name + ".wayIndex";
            data = new string[wayFinder2.Count()];
            j = 0;
            foreach(var wf in wayFinder2)
            {
                data[j] = wf.Key + ":" + wf.Value;
                j++;
            }
            System.IO.File.WriteAllLines(filename, data);

            filename = fi.Name + ".nodeIndex";
            data = new string[nodeFinder2.Count()];
            j = 0;
            foreach (var wf in nodeFinder2)
            {
                data[j] = wf.Key + ":" + wf.Value.Item1  + ":" + wf.Value.Item2;
                j++;
            }
            System.IO.File.WriteAllLines(filename, data);
        }

        public void LoadBlockInfo()
        {
            try
            {
                string filename = fi.Name + ".blockinfo";
                string[] data = System.IO.File.ReadAllLines(filename);
                blockPositions = new Dictionary<long, long>(data.Length);
                blockSizes = new Dictionary<long, int>(data.Length);

                for (int i = 0; i < data.Count(); i++)
                {
                    string[] subdata = data[i].Split(":");
                    blockPositions[i] = long.Parse(subdata[1]);
                    blockSizes[i] = int.Parse(subdata[2]);
                }

                filename = fi.Name + ".relationIndex";
                data = System.IO.File.ReadAllLines(filename);
                foreach (var line in data)
                {
                    string[] subData2 = line.Split(":");
                    relationFinder.TryAdd(long.Parse(subData2[0]), Tuple.Create(long.Parse(subData2[1]), int.Parse(subData2[2])));
                }

                filename = fi.Name + ".wayIndex";
                data = System.IO.File.ReadAllLines(filename);
                foreach (var line in data)
                {
                    string[] subData2 = line.Split(":");
                    wayFinder2.TryAdd(long.Parse(subData2[0]), long.Parse(subData2[1]));
                }

                filename = fi.Name + ".nodeIndex";
                data = System.IO.File.ReadAllLines(filename);
                foreach (var line in data)
                {
                    string[] subData2 = line.Split(":");
                    nodeFinder2.TryAdd(long.Parse(subData2[0]), Tuple.Create(long.Parse(subData2[1]), long.Parse(subData2[2])));
                }
            }
            catch(Exception ex)
            {
                return;
            }
        }

        public void SaveCurrentBlock(long blockID)
        {
            string filename = fi.Name + ".progress";
            System.IO.File.WriteAllText(filename, blockID.ToString());
        }

        public long FindLastCompletedBlock()
        {
            string filename = fi.Name + ".progress";
            long blockID = long.Parse(System.IO.File.ReadAllText(filename));
            return blockID;
        }

        public void CleanupFiles()
        {
            foreach (var file in System.IO.Directory.EnumerateFiles(".", "*.blockInfo"))
                System.IO.File.Delete(file);

            foreach (var file in System.IO.Directory.EnumerateFiles(".", "*.relationIndex"))
                System.IO.File.Delete(file);

            foreach (var file in System.IO.Directory.EnumerateFiles(".", "*.nodeIndex"))
                System.IO.File.Delete(file);

            foreach (var file in System.IO.Directory.EnumerateFiles(".", "*.wayIndex"))
                System.IO.File.Delete(file);

            foreach (var file in System.IO.Directory.EnumerateFiles(".", "*.progress"))
                System.IO.File.Delete(file);
        }

        //public void ListMostAccessedBlocks()
        //{
        //    var entries = accessCounter.OrderByDescending(a => a.Item2).ToList();
        //    foreach (var item in entries.Take(20))
        //        Console.WriteLine("Block " + item.Item1 + " : " + item.Item2);
        //}
    }
}
