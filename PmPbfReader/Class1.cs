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
    //I think this is now returning reasonable accurate data, though it's currently slow compared
    //to baseline OsmSharp. Ready to clean up and do a comparison check to see how much performance
    //i lose doing things block by block.

    //rough usage plan:
    //Open a file - locks access
    //IndexFileParallel - can now search the file. required call. Fills stuff that will persist in memory, so this is the limit on filesize now.
    //GetGeometryFromBlock - create everything from the given block as OSMSharp.CompleteGeo. Could write that to its own file and be able to resume later.

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
    //index-parallel: 2:04 ==> 40 seconds when correctly processing individual blocks. MAYBE i should drop that ContainsKey check now that the cause was found....
    //index-blocks: 0:00:60
    //load: :26
    //load-parallel: :11
    // lastblock: 8:02
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
    //This is approaching usable.

    //This should work with OsmSharp objects, to avoid rewriting the rest of my app.

    //A: could write relations to file immediately once they're completed, rather than holding them in memory. (sort of. probably not the best plan for a DLL)
    //B: Could store Ways/Nodes that have been created, and re-reference them if they're called later rather than recreating them from the source block each time.
    //C: could do 1 pass instead of 3?
    //D: more parallel stuff?.
    //e: hot path says GetNode and FindBlockKeyForNode are the time consuming pieces. Inflating all nodes is a block is slower than getting each one individually
    //e2: maybe i could get all nodes in a block at once, and process them out as I find them in the block? instead of parsing the list per node
    //Would need to look up all node blocks first and store results in a dictionary<block, nodeid>, then pass that to a new version of GetNodes() which returns a dictionary<id, node>;
    public class PbfReader
    {
        FileInfo fi;
        FileStream fs;

        //these are osmId, <blockId, primitiveGroupID>
        //but primGroupID is always 0, so i should switch this around to just blockID
        ConcurrentDictionary<long, Tuple<long, int>> relationFinder = new ConcurrentDictionary<long, Tuple<long, int>>();
        ConcurrentDictionary<long, Tuple<long, int>> wayFinder = new ConcurrentDictionary<long, Tuple<long, int>>();
        ConcurrentDictionary<long, Tuple<long, int>> nodeFinder = new ConcurrentDictionary<long, Tuple<long, int>>();

        //this is blockId, <minNode, maxNode>.
        ConcurrentDictionary<long, Tuple<long, long>> nodeFinder2 = new ConcurrentDictionary<long, Tuple<long, long>>();

        Dictionary<long, long> blockPositions = new Dictionary<long, long>();
        Dictionary<long, int> blockSizes = new Dictionary<long, int>();

        //For testing purposes, not intended to be actually used as a whole memory store of a file.
        //Dictionary<long, PrimitiveBlock> loadedBlocks = new Dictionary<long, PrimitiveBlock>();
        ConcurrentDictionary<long, PrimitiveBlock> loadedBlocks = new ConcurrentDictionary<long, PrimitiveBlock>();

        private PrimitiveBlock _block = new PrimitiveBlock();
        private BlobHeader _header = new BlobHeader();

        long waysStartAt = 0;
        long relationsStartAt = 0;
        //long blockCounter = 0;

        //I will use the write lock to make sure threads don't read the wrong data
        //the names will be misleading, since i dont want to use overlapping IO on these even though
        //the docs say I could, since I'd need to Seek() to a position and then read and its possible
        //threads would change the Seek point before the ReadAsync was called.
        System.Threading.ReaderWriterLockSlim msLock = new System.Threading.ReaderWriterLockSlim();

        public long BlockCount()
        {
            return blockPositions.Count();
        }

        public void Open(string filename)
        {
            fi = new FileInfo(filename);
            Console.WriteLine(fi.FullName + " | " + fi.Length);
            fs = File.OpenRead(filename);
        }

        public void Close()
        {
            fs.Close();
            fs.Dispose();
        }

        public void LoadWholeFileParallel()
        {
            loadedBlocks = new ConcurrentDictionary<long, PrimitiveBlock>();
            //as index file, but saves the decoded block in RAM
            BlobHeader bh = new BlobHeader();
            Blob b = new Blob();

            //int outRead = 0;
            long blockCounter = 0;

            HeaderBlock hb = new HeaderBlock();
            PrimitiveBlock pb = new PrimitiveBlock();

            fs.Position = 0;

            //Only one OsmHeader, at the start
            bh = Serializer.DeserializeWithLengthPrefix<BlobHeader>(fs, PrefixStyle.Fixed32BigEndian);
            hb = Serializer.Deserialize<HeaderBlock>(fs, length: bh.datasize); //only one of these per file    
            Console.WriteLine(hb.source + "|" + hb.writingprogram);
            Action<long, byte[]> save = (a, b) => SaveBlock(a, b);

            List<Task> pendingSaves = new List<Task>();
            //header block left out intentionally, start data blocks at 1
            while (fs.Position != fs.Length)
            {
                blockCounter++;
                bh = Serializer.DeserializeWithLengthPrefix<BlobHeader>(fs, PrefixStyle.Fixed32BigEndian);

                byte[] thisblob = new byte[bh.datasize];
                fs.Read(thisblob, 0, bh.datasize);

                long valToPass = blockCounter;
                pendingSaves.Add(System.Threading.Tasks.Task.Run(() => { save(valToPass, thisblob); }));
            }
            Task.WaitAll(pendingSaves.ToArray());
        }

        public void IndexFile()
        {
            fs.Position = 0;
            long blockCounter = 0;
            blockPositions = new Dictionary<long, long>();
            blockSizes = new Dictionary<long, int>();
            relationFinder = new ConcurrentDictionary<long, Tuple<long, int>>();
            wayFinder = new ConcurrentDictionary<long, Tuple<long, int>>();
            nodeFinder = new ConcurrentDictionary<long, Tuple<long, int>>();
            nodeFinder2 = new ConcurrentDictionary<long, Tuple<long, long>>();

            BlobHeader bh = new BlobHeader();
            Blob b = new Blob();

            //int outRead = 0;

            HeaderBlock hb = new HeaderBlock();
            PrimitiveBlock pb = new PrimitiveBlock();

            //Only one OsmHeader, at the start
            bh = Serializer.DeserializeWithLengthPrefix<BlobHeader>(fs, PrefixStyle.Fixed32BigEndian);
            hb = Serializer.Deserialize<HeaderBlock>(fs, length: bh.datasize); //only one of these per file    
            Console.WriteLine(hb.source + "|" + hb.writingprogram);
            blockPositions.Add(0, fs.Position);
            blockSizes.Add(0, bh.datasize);

            //header block left out intentionally, start data blocks at 1
            while (fs.Position != fs.Length)
            {
                blockCounter++;
                bh = Serializer.DeserializeWithLengthPrefix<BlobHeader>(fs, PrefixStyle.Fixed32BigEndian);
                blockPositions.Add(blockCounter, fs.Position);
                blockSizes.Add(blockCounter, bh.datasize);

                byte[] thisblob = new byte[bh.datasize];
                fs.Read(thisblob, 0, bh.datasize);
                var ms1 = new MemoryStream(thisblob);

                b = Serializer.Deserialize<Blob>(ms1);
                var ms = new MemoryStream(b.zlib_data);
                var dms = new ZlibStream(ms, CompressionMode.Decompress);

                int groupCounter = 0;
                //_runtimeTypeModel.Deserialize<PrimitiveBlock>(dms, pb);
                pb = Serializer.Deserialize<PrimitiveBlock>(dms);
                foreach (var group in pb.primitivegroup)
                {
                    //if (waysStartAt == 0 && group.ways.Count > 0)
                    //  waysStartAt = blockCounter;
                    //if (relationsStartAt == 0 && group.relations.Count > 0)
                    //  relationsStartAt = blockCounter;

                    foreach (var r in group.relations)
                    {
                        relationFinder.TryAdd(r.id, new Tuple<long, int>(blockCounter, groupCounter));
                    }

                    foreach (var w in group.ways)
                    {
                        //if (wayFinder.ContainsKey(w.id)) continue; //duplicates and different versions are ignored
                        wayFinder.TryAdd(w.id, new Tuple<long, int>(blockCounter, groupCounter));
                    }

                    long nodecounter = 0;
                    long minNode = long.MaxValue;
                    long maxNode = long.MinValue;
                    if (group.dense != null)
                    {
                        foreach (var n in group.dense.id)
                        {
                            //While i could probably optimize this by just using the first value as min
                            //and the sum of all values as max, its possible that there's a -X value
                            //that goes under it, or that the last value in the list is negative.
                            //This is the correct way to do this, though i could ponder using the shortcut idea
                            //for experimenting.
                            nodecounter += n;
                            //if (nodeFinder.ContainsKey(nodecounter)) continue;
                            //nodeFinder.TryAdd(nodecounter, new Tuple<long, int>(blockCounter, groupCounter));
                            if (nodecounter < minNode)
                                minNode = nodecounter;
                            if (nodecounter > maxNode)
                                maxNode = nodecounter;
                        }
                        nodeFinder2.TryAdd(blockCounter, new Tuple<long, long>(minNode, maxNode));

                    }
                    groupCounter++;
                }
            }
        }

        public void IndexFileParallel()
        {
            fs.Position = 0;
            long blockCounter = 0;
            blockPositions = new Dictionary<long, long>();
            blockSizes = new Dictionary<long, int>();
            relationFinder = new ConcurrentDictionary<long, Tuple<long, int>>();
            wayFinder = new ConcurrentDictionary<long, Tuple<long, int>>();
            nodeFinder = new ConcurrentDictionary<long, Tuple<long, int>>();
            nodeFinder2 = new ConcurrentDictionary<long, Tuple<long, long>>();

            BlobHeader bh = new BlobHeader();
            Blob b = new Blob();

            //int outRead = 0;

            HeaderBlock hb = new HeaderBlock();
            PrimitiveBlock pb = new PrimitiveBlock();

            //Only one OsmHeader, at the start
            bh = Serializer.DeserializeWithLengthPrefix<BlobHeader>(fs, PrefixStyle.Fixed32BigEndian);
            hb = Serializer.Deserialize<HeaderBlock>(fs, length: bh.datasize); //only one of these per file    
            Console.WriteLine(hb.source + "|" + hb.writingprogram);
            blockPositions.Add(0, fs.Position);
            blockSizes.Add(0, bh.datasize);

            List<Task> waiting = new List<Task>();

            //header block left out intentionally, start data blocks at 1
            while (fs.Position != fs.Length)
            {
                blockCounter++;
                bh = Serializer.DeserializeWithLengthPrefix<BlobHeader>(fs, PrefixStyle.Fixed32BigEndian);
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
                    if (waysStartAt == 0 && group.ways.Count > 0)
                        waysStartAt = blockCounter;
                    if (relationsStartAt == 0 && group.relations.Count > 0)
                        relationsStartAt = blockCounter;


                    foreach (var r in pb2.primitivegroup[0].relations)
                    {
                        relationFinder.TryAdd(r.id, new Tuple<long, int>(passedBC, 0));
                    }

                    foreach (var w in pb2.primitivegroup[0].ways)
                    {
                        //if (wayFinder.ContainsKey(w.id)) continue; //duplicates and different versions are ignored
                        wayFinder.TryAdd(w.id, new Tuple<long, int>(passedBC, 0));
                    }

                    long nodecounter = 0;
                    long minNode = long.MaxValue;
                    long maxNode = long.MinValue;
                    if (pb2.primitivegroup[0].dense != null)
                    {
                        foreach (var n in pb2.primitivegroup[0].dense.id)
                        {
                            nodecounter += n;
                            //if (nodeFinder.ContainsKey(nodecounter)) continue;
                            //nodeFinder.TryAdd(nodecounter, new Tuple<long, int>(passedBC, 0));
                            if (nodecounter < minNode)
                                minNode = nodecounter;
                            if (nodecounter > maxNode)
                                maxNode = nodecounter;
                        }
                        nodeFinder2.TryAdd(passedBC, new Tuple<long, long>(minNode, maxNode));
                    }
                    //groupCounter++;
                });

                waiting.Add(tasked);
            }
            Task.WaitAll(waiting.ToArray());
        }

        public void IndexFileBlocks()
        {
            //Only fills in blockPositions and blockSizes
            //Most files seems to be sorted with the Relations at the end
            //then Ways before those and node only entries last.
            //So i want to see if blocks are self-contained fully, and 
            //read relations first, then ways, then skim nodes for data.

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

        //NOTE: once this is done with the memory stream, it could split off the remaining work to a Task
        //as a possible small multithread optimization. Test this singlethread against the split versions
        //GetBlockBytes (singlethread) and DecodeBlock(taskable)
        public PrimitiveBlock GetBlock(long blockId)
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


            // its the _runtimeTypeModel that keeps everything in Memory from previous reads
            //and thats whats messing up my primitiveBlock count
            //var pulledBlock = new PrimitiveBlock();
            // _runtimeTypeModel.Deserialize<PrimitiveBlock>(dms2, pulledBlock);
            var pulledBlock = Serializer.Deserialize<PrimitiveBlock>(dms2);
            Console.WriteLine("Block " + blockId + " loaded to RAM");
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

            //var pulledBlock = new PrimitiveBlock();
            //_runtimeTypeModel.Deserialize<PrimitiveBlock>(dms2, pulledBlock);
            var pulledBlock = Serializer.Deserialize<PrimitiveBlock>(dms2);
            return pulledBlock;
        }

        public void SaveBlock(long blockId, byte[] blockBytes)
        {
            loadedBlocks.TryAdd(blockId, DecodeBlock(blockBytes));
        }

        public OsmSharp.Complete.CompleteRelation GetRelation(long relationId, ref ConcurrentDictionary<long, PrimitiveBlock> activeBlocks)
        {
            try
            {
                //Console.WriteLine("getting relation " + relationId);
                //Run after indexing file
                //load only relevant blocks for this entry
                var relationBlockValues = relationFinder[relationId];
                PrimitiveBlock relationBlock;
                if (activeBlocks.ContainsKey(relationBlockValues.Item1))
                    relationBlock = activeBlocks[relationBlockValues.Item1];
                else
                {
                    relationBlock = GetBlock(relationBlockValues.Item1);
                    activeBlocks.TryAdd(relationBlockValues.Item1, relationBlock);
                }

                var relPrimGroup = relationBlock.primitivegroup[0];
                var rel = relPrimGroup.relations.Where(r => r.id == relationId).FirstOrDefault();
                //finally have the core item

                if (rel.keys.Count == 0) //I cant use untagged areas for anything.
                    return null;

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
                            neededBlocks.Add(FindBlockKeyForNode(idToFind, ref activeBlocks));
                            break;
                        case Relation.MemberType.WAY:
                            neededBlocks.Add(wayFinder[idToFind].Item1);
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
                        activeBlocks.TryAdd(nb, GetBlock(nb));
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
                                loadedNodes.Add(idToFind, GetNode(idToFind, ref activeBlocks, true));
                            break;
                        case Relation.MemberType.WAY:
                            if (!loadedWays.ContainsKey(idToFind))
                                loadedWays.Add(idToFind, GetWay(idToFind, ref activeBlocks, false));
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
                    switch (typeToFind)
                    {
                        case Relation.MemberType.NODE:
                            c.Member = loadedNodes[idToFind];
                            c.Role = "Node";
                            break;
                        case Relation.MemberType.WAY:
                            c.Member = loadedWays[idToFind];
                            c.Role = "Way";
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

        public OsmSharp.Complete.CompleteRelation GetRelationFromBlock(Relation rel, PrimitiveBlock block, Dictionary<long, OsmSharp.Node> nodeList, List<OsmSharp.Complete.CompleteOsmGeo> finishedItems)
        {
            //Ive got all the blocks directly referenced by this relation. But i need to do at least one more pass
            //because Ways may or may not need new blocks too.
            OsmSharp.Complete.CompleteRelation r = new OsmSharp.Complete.CompleteRelation();
            r.Id = rel.id;
            r.Tags = new OsmSharp.Tags.TagsCollection();

            for (int i = 0; i < rel.keys.Count(); i++)
            {
                int key = (int)rel.keys[i];
                int val = (int)rel.vals[i];
                string keyEntry = System.Text.Encoding.UTF8.GetString(block.stringtable.s[key]);
                string valEntry = System.Text.Encoding.UTF8.GetString(block.stringtable.s[val]);
                r.Tags.Add(new OsmSharp.Tags.Tag(keyEntry, valEntry));
            }

            //final pass, to make sure elements are in the correct order
            List<OsmSharp.Complete.CompleteRelationMember> crms = new List<OsmSharp.Complete.CompleteRelationMember>();
            for (int i = 0; i < rel.memids.Count; i++)
            {
                long idToFind = rel.memids[i];
                Relation.MemberType typeToFind = rel.types[i];
                OsmSharp.Complete.CompleteRelationMember c = new OsmSharp.Complete.CompleteRelationMember();
                switch (typeToFind)
                {
                    case Relation.MemberType.NODE:
                        c.Member = nodeList[idToFind];
                        break;
                    case Relation.MemberType.WAY:
                        c.Member = finishedItems.Where(f => f.Id == idToFind && f.Type == OsmSharp.OsmGeoType.Way).FirstOrDefault();
                        break;
                    case Relation.MemberType.RELATION:
                        c.Member = finishedItems.Where(f => f.Id == idToFind && f.Type == OsmSharp.OsmGeoType.Relation).FirstOrDefault();
                        break;
                }
                crms.Add(c);
            }
            r.Members = crms.ToArray();

            return r;
        }

        public OsmSharp.Complete.CompleteWay GetWay(long wayId, ref ConcurrentDictionary<long, PrimitiveBlock> activeBlocks, bool skipUntagged)
        {
            try
            {
                //Console.WriteLine("getting way " + wayId);
                //Run after indexing file
                //load only relevant blocks for this entry
                var wayBlockValues = wayFinder[wayId];
                PrimitiveBlock wayBlock;
                if (activeBlocks.ContainsKey(wayBlockValues.Item1))
                    wayBlock = activeBlocks[wayBlockValues.Item1];
                else
                {
                    wayBlock = GetBlock(wayBlockValues.Item1);
                    activeBlocks.TryAdd(wayBlockValues.Item1, wayBlock);
                }
                var wayPrimGroup = wayBlock.primitivegroup[0];
                var way = wayPrimGroup.ways.Where(w => w.id == wayId).FirstOrDefault();
                //finally have the core item

                if (skipUntagged && way.keys.Count == 0) 
                    return null;

                //more deltas 
                long idToFind = 0;
                List<long> neededBlocks = new List<long>();
                //blockId, nodeID
                List<Tuple<long, long>> nodesPerBlock = new List<Tuple<long, long>>();

                for (int i = 0; i < way.refs.Count; i++)
                {
                    idToFind += way.refs[i];
                    var blockID = FindBlockKeyForNode(idToFind, ref activeBlocks);
                    neededBlocks.Add(blockID);
                    nodesPerBlock.Add(Tuple.Create(blockID, idToFind));
                }

                neededBlocks = neededBlocks.Distinct().ToList();
                var nodesByBlock = nodesPerBlock.ToLookup(k => k.Item1, v => v.Item2);

                foreach (var nb in neededBlocks)
                {
                    if (!activeBlocks.ContainsKey(nb))
                    {
                        activeBlocks.TryAdd(nb, GetBlock(nb));
                    }
                }
                //Now I have the data needed to fill in nodes for a way


                OsmSharp.Complete.CompleteWay finalway = new OsmSharp.Complete.CompleteWay();
                finalway.Id = wayId;
                finalway.Tags = new OsmSharp.Tags.TagsCollection();

                //skipUntagged is false from GetRelation, so we can ignore tag data in that case as well.
                if (skipUntagged) //If we want to skip the untagged entries, we also want to fill in tags. If we want every Way regardless, we don't need the tag values.
                for (int i = 0; i < way.keys.Count(); i++)
                {
                    finalway.Tags.Add(new OsmSharp.Tags.Tag(System.Text.Encoding.UTF8.GetString(wayBlock.stringtable.s[(int)way.keys[i]]), System.Text.Encoding.UTF8.GetString(wayBlock.stringtable.s[(int)way.vals[i]])));
                }

                //new way
                List<OsmSharp.Node> nodeList = new List<OsmSharp.Node>();
                //<blockId, <nodeID, Node>>
                Dictionary<long, Dictionary<long, OsmSharp.Node>> neededNodes = new Dictionary<long, Dictionary<long, OsmSharp.Node>>();
                foreach (var block in nodesByBlock)
                {
                    var someNodes = GetAllNeededNodesInBlock(ref activeBlocks, block.Key, block.ToList());
                    if (someNodes == null)
                        throw new Exception("Couldn't load all nodes from a block");
                    neededNodes.Add(block.Key, someNodes);
                }

                idToFind = 0;
                foreach (var node in way.refs)
                {
                    idToFind += node;
                    var subEntry = neededNodes.Where(n => n.Value.ContainsKey(idToFind)).First(); //This shoudl be much faster than searching all entries.
                    nodeList.Add(subEntry.Value[idToFind]);
                }

                //old way
                //idToFind = 0;
                //foreach (var node in way.refs)
                //{
                //    idToFind += node;
                //    var blockId = FindBlockKeyForNode(idToFind, ref activeBlocks);
                //    nodeList.Add(GetNode(idToFind, ref activeBlocks, true));
                //}
                finalway.Nodes = nodeList.ToArray();

                //Console.WriteLine("got way " + wayId);
                return finalway;
            }
            catch (Exception ex)
            {
                return null; //Failed to get way, probably because a node didn't exist in the file.
            }
        }

        public OsmSharp.Complete.CompleteWay GetWayFromBlock(Way way, PrimitiveBlock block, Dictionary<long, OsmSharp.Node> currentNodes)
        {
            OsmSharp.Complete.CompleteWay finalway = new OsmSharp.Complete.CompleteWay();
            finalway.Id = way.id;
            for (int i = 0; i < way.keys.Count(); i++)
            {
                finalway.Tags.Add(new OsmSharp.Tags.Tag(System.Text.Encoding.UTF8.GetString(block.stringtable.s[(int)way.keys[i]]), System.Text.Encoding.UTF8.GetString(block.stringtable.s[(int)way.vals[i]])));
            }

            List<OsmSharp.Node> nodeList = new List<OsmSharp.Node>();
            foreach (var node in way.refs)
            {
                nodeList.Add(currentNodes[node]);
            }
            finalway.Nodes = nodeList.ToArray();

            return finalway;
        }
        //public OsmSharp.Node GetNodeFromSmallNode(SmallNode node)
        //{
        //    return new OsmSharp.Node() { Id = node.id, Latitude = node.lat, Longitude = node.lon };
        //}

        public OsmSharp.Node GetNode(long nodeId, ref ConcurrentDictionary<long, PrimitiveBlock> activeBlocks, bool skipTags = true)
        {
            //Console.WriteLine("getting node " + nodeId);
            //Run after indexing file
            //load only relevant blocks for this entry
            var nodeBlockValues = FindBlockKeyForNode(nodeId, ref activeBlocks);

            PrimitiveBlock nodeBlock;
            if (activeBlocks.ContainsKey(nodeBlockValues))
                nodeBlock = activeBlocks[nodeBlockValues];
            else
            {
                nodeBlock = GetBlock(nodeBlockValues);
                activeBlocks.TryAdd(nodeBlockValues, nodeBlock);
            }
            var nodePrimGroup = nodeBlock.primitivegroup[0];

            long nodeCounter = 0;
            int index = -1;
            long latDelta = 0;
            long lonDelta = 0;
            while (nodeCounter != nodeId)
            {
                index += 1;
                nodeCounter += nodePrimGroup.dense.id[index];
                latDelta += nodePrimGroup.dense.lat[index];
                lonDelta += nodePrimGroup.dense.lon[index];
            }

            OsmSharp.Node filled = new OsmSharp.Node();
            filled.Id = nodeId;
            filled.Latitude = DecodeLatLon(latDelta, nodeBlock.lat_offset, nodeBlock.granularity);
            filled.Longitude = DecodeLatLon(lonDelta, nodeBlock.lon_offset, nodeBlock.granularity);

            if (!skipTags)
            {
                var tagData = nodePrimGroup.dense.keys_vals[index];
                // foreach (var t in nodeBlock..primitivegroup[index].)

                //tags are dumb and they all have to be unpacked at once. can't do this simple lookup i wanted.
                int tagcounter = 0;//The first tag that will be part of this node.
                int tagIndexTracker = 0; //how many nodes worth of tags we've skipped so far.
                while (true)
                {
                    if (nodePrimGroup.dense.keys_vals[tagcounter] == 0)
                        tagIndexTracker++;

                    tagcounter++;
                    if (tagIndexTracker == index)
                        break;
                }

                //now, start loading keys/values

                while (true)
                {
                    int key = nodePrimGroup.dense.keys_vals[tagcounter];
                    int value = nodePrimGroup.dense.keys_vals[tagcounter + 1];
                    if (key == 0)
                        break;
                    tagcounter += 2;
                    filled.Tags.Add(new OsmSharp.Tags.Tag(System.Text.Encoding.UTF8.GetString(nodeBlock.stringtable.s[key]), System.Text.Encoding.UTF8.GetString(nodeBlock.stringtable.s[value])));
                }
            }
            //Console.WriteLine("got node " + nodeId);
            return filled;
        }

        public Dictionary<long, OsmSharp.Node> GetAllNeededNodesInBlock(ref ConcurrentDictionary<long, PrimitiveBlock> activeBlocks, long blockId, List<long> nodeIds)
        {
            //GetWay() has already ensured that activeBlocks contains the data I need, so i can skip checking it.
            Dictionary<long, OsmSharp.Node> results = new Dictionary<long, OsmSharp.Node>();

            var block = activeBlocks[blockId];
            var group = block.primitivegroup[0];

            int index = -1;
            long nodeCounter = 0;
            long latDelta = 0;
            long lonDelta = 0;
            while (results.Count < nodeIds.Count())
            {
                index++;
                nodeCounter += group.dense.id[index];
                latDelta = group.dense.lat[index];
                lonDelta = group.dense.lon[index];

                if (nodeIds.Contains(nodeCounter))
                {
                    OsmSharp.Node filled = new OsmSharp.Node();
                    filled.Id = nodeCounter;
                    filled.Latitude = DecodeLatLon(latDelta, block.lat_offset, block.granularity);
                    filled.Longitude = DecodeLatLon(lonDelta, block.lon_offset, block.granularity);
                    //filled.Latitude = DecodeLatLon(block.lat_offset, block.primitivegroup[0].dense.lat[index], block.granularity);
                    //filled.Longitude = DecodeLatLon(block.lon_offset, block.primitivegroup[0].dense.lon[index], block.granularity);
                    results.Add(nodeCounter, filled);
                }
            }
            return results;
        }

        public OsmSharp.Node GetNodeFromBlock(long nodeId, PrimitiveBlock block, PrimitiveGroup group, bool skipTags = true)
        {

            OsmSharp.Node filled = new OsmSharp.Node();
            filled.Id = nodeId;

            int index = -1;
            long nodeCounter = 0;
            while (nodeCounter != nodeId)
            {
                index++;
                nodeCounter += group.dense.id[index];
            }

            filled.Latitude = DecodeLatLon(group.dense.lat[index], block.lat_offset, block.granularity);
            filled.Longitude = DecodeLatLon(group.dense.lon[index], block.lon_offset, block.granularity);

            if (!skipTags)
            {
                var tagData = group.dense.keys_vals[index];
                // foreach (var t in nodeBlock..primitivegroup[index].)

                //tags are dumb and they all have to be unpacked at once. can't do this simple lookup i wanted.
                int tagcounter = 0;//The first tag that will be part of this node.
                int tagIndexTracker = 0; //how many nodes worth of tags we've skipped so far.
                while (true)
                {
                    if (group.dense.keys_vals[tagcounter] == 0)
                        tagIndexTracker++;

                    tagcounter++;
                    if (tagIndexTracker == index)
                        break;
                }

                //now, start loading keys/values

                while (true)
                {
                    int key = group.dense.keys_vals[tagcounter];
                    int value = group.dense.keys_vals[tagcounter + 1];
                    if (key == 0)
                        break;
                    tagcounter += 2;
                    filled.Tags.Add(new OsmSharp.Tags.Tag(System.Text.Encoding.UTF8.GetString(block.stringtable.s[key]), System.Text.Encoding.UTF8.GetString(block.stringtable.s[value])));
                }
            }

            return filled;
        }

        public long FindBlockKeyForNode(long nodeId, ref ConcurrentDictionary<long, PrimitiveBlock> activeBlocks)
        {

            //search inflated nodes first, probably faster to hash against those than to count to 8k each entry
            //NOPE, we look there if we already know its block.
            //foreach (var smallCheck in SmallNodes)
            //if (smallCheck.Value.ContainsKey(nodeId))
            //return smallCheck.Key;

            foreach (var nodelist in nodeFinder2)
            {
                //key is block id
                //value is the tuple list. 1 is min, 2 is max.
                if (nodelist.Value.Item1 > nodeId) //this node's minimum is larger than our node, skip
                    continue;

                if (nodelist.Value.Item2 < nodeId) //this node's maximum is smaller than our node, skip
                    continue;

                //This block can potentially hold the node in question.
                if (!activeBlocks.ContainsKey(nodelist.Key))
                    activeBlocks.TryAdd(nodelist.Key, GetBlock(nodelist.Key));

                var nodeBlock = activeBlocks[nodelist.Key];
                var group = nodeBlock.primitivegroup[0];

                long nodecounter = 0;
                int nodeIndex = -1;
                //as much as i want to tree search this, the negative delta values really mess that up, since there;s
                //no guarentee nodes are sorted by id
                while (nodeIndex < 7999) //groups can only have 8000 entries.
                {
                    nodeIndex++;
                    nodecounter += group.dense.id[nodeIndex];
                    if (nodecounter == nodeId)
                        return nodelist.Key;
                }
            }


            //couldnt find this node
            throw new Exception("Node Not Found");

        }

        public List<OsmSharp.Complete.CompleteOsmGeo> GetGeometryFromBlock(long blockId)
        {
            //This grabs the last block, populates everything in it to an OsmSharp.Complete object
            //and returns that list. Removes the block from memory once that's done.
            //This is the 'main' function I think i'll use to do most of the work originally.
            //I want this to be self-contained RAM wise, so that everything this block references eventually
            //gets pulled in, and can be dropped from memory when this function ends.
            try
            {

                //var lastBlockID = blockPositions.Keys.Max();
                var block = GetBlock(blockId);

                List<OsmSharp.Complete.CompleteOsmGeo> results = new List<OsmSharp.Complete.CompleteOsmGeo>();
                List<OsmSharp.Node> nodeList = new List<OsmSharp.Node>();
                //loadedNodes is<blockId, <nodeId, SmallNode>>
                //ConcurrentDictionary<long, Dictionary<long, SmallNode>> loadedNodes = new ConcurrentDictionary<long, Dictionary<long, SmallNode>>();

                ConcurrentDictionary<long, PrimitiveBlock> activeBlocks = new ConcurrentDictionary<long, PrimitiveBlock>();
                activeBlocks.TryAdd(blockId, block);

                foreach (var primgroup in block.primitivegroup)
                {
                    if (primgroup.relations != null && primgroup.relations.Count() > 0)
                    {
                        //foreach (var r in primgroup.relations)
                        Parallel.ForEach(primgroup.relations, r =>
                        {
                            var relation = GetRelation(r.id, ref activeBlocks);
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
                            var way = GetWay(w.id, ref activeBlocks, true); //here, I skip untagged geometry.
                            if (way != null)
                                results.Add(way);
                        });
                    }
                    else
                    {
                        //I need a different plan for Nodes.
                        //They need to be skimmed for tags, then filtered to ones with relevant tags to import.
                        //This might be a separate calls.

                        //var nodes = InflateNodes(block);
                        //loadedNodes.TryAdd(blockId, nodes);
                        //node logic will need a different plan. nodes are OsmCompleteGeo items.
                        //foreach (var n in primgroup.dense.id)
                        //{
                        //var node = GetNodeFromSmallNode(n, ref activeBlocks, false);
                        //if (node != null)
                        //nodeList.Add(node);
                        //}
                    }
                }
                var count = (block.primitivegroup[0].relations.Count > 0 ? block.primitivegroup[0].relations.Count :
                    block.primitivegroup[0].ways.Count > 0 ? block.primitivegroup[0].ways.Count :
                    block.primitivegroup[0].dense.id.Count);

                Console.WriteLine("block " + blockId + ":" + results.Count() + " items out of " + count + " created without errors");
                return results;
            }
            catch (Exception ex)
            {
                return null;
            }

        }



        public record SmallNode(long id, double lat, double lon);

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
    }
}
