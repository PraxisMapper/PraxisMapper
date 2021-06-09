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
//big chunks of this copied from OsmSharp.IO.Pbf.Encoder
using System.Linq.Expressions;

namespace PmPbfReader
{
    //I think this is now returning reasonable accurate data, though it's currently very slow compared
    //to baseline OsmSharp. I can start the optimization pass now.

    // ponder just storing nodes instead of an index for them. they're 2 doubles and a long each, if i ignore tags.
    //versus a hash entry and 2 longs for the full index

    //I thought I didnt want to do this, but i might have a little fun working this out on my own.
    //Read through a PBF file, and only pull out the parts I actually care about
    //That should theoretically be faster than OsmSharp's underlying reader
    //Feature notes
    //A) Store a list(hashmap?) of which items are stored in which block for later access (dictionary with tuples)
    //B) only materialize values that PM is concerned with. Skip other stuff.
    //Rough plan for feature work
    //Open file stream
    //read headers
    //track entries
    //decode entries
    //All of which can be tested in the test perf app.

    //todo check on what parts can or cant multithread. pretty sure memorystream isnt threadsafe
    //but collections might be? might need concurrentdictionary?
    //Also look at ReadAsync method on FileStream for tasked methods

    //note 2: doing a 'if ContainsKey continue' check is much faster than just doing TryAdd for each object. 
    //twice as fast on Delaware. Probably bigger improvements on bigger files.

    //should see if indexing nodes is better by just taking lowest and highest node id per block
    //rather than tracking every single node, then checking blocks that cover the range for a node.

    //rough usage plan:
    //Open a file - locks access
    //IndexFileParallel - can now search the file
    //PopulateNextBlock - create everything from the last block in the dictionary.

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
    //7: (undid previous change)

    //ohio, starting at 7:
    //7: 1:30 relations, ways 10.5 seconds.
    //This is approaching usable.

    //when searching a block for nodes, can I use a tree search?
    //ex: start at half, see if its higher/lower, then go halfway there, repeat 14 times and then
    //be on or near the node index?
    //probaby not, for the same reason i cant just use first/total for min/max. but its close.
    //might still be faster to do the 14 step tree search and then iterate out from there than skim 8000 entries in a list.
    //do this later, after baseline functionality exists

    //which parts of GetGeometryFromLastBlock can be parallel? probably loading stuff from blocks in memory
    //probably not reading blocks from file.

    //This should work with OsmSharp objects, to avoid rewriting the rest of my app.

    //A: could write relations to file immediately once they're completed, rather than holding them in memory. (sort of. probably not the best plan for a DLL)
    //B: Could store Ways/Nodes that have been created, and re-reference them if they're called later rather than recreating them from the source block each time.
    //B2: For a block of Nodes, could just create all 8k nodes at that time, instead of reprocessing the block when i need 1
    //C: could do 1 pass instead of 3?
    //D: more parallel stuff.
    //e: hot path says GetNode and FindBlockKeyForNode are the time consuming pieces.
    public class PbfReader
    {
        FileInfo fi;
        FileStream fs;

        //These might need to become 3 value tuples <OsmElementId, blockCountId, primitiveGroupId>

        //Dictionary<long, Tuple<long, int>> relationFinder = new Dictionary<long, Tuple<long, int>>();
        //Dictionary<long, Tuple<long, int>> wayFinder = new Dictionary<long, Tuple<long, int>>();
        //Dictionary<long, Tuple<long, int>> nodeFinder = new Dictionary<long, Tuple<long, int>>();

        //these are osmId, <blockId, primitiveGroupID>
        ConcurrentDictionary<long, Tuple<long, int>> relationFinder = new ConcurrentDictionary<long, Tuple<long, int>>();
        ConcurrentDictionary<long, Tuple<long, int>> wayFinder = new ConcurrentDictionary<long, Tuple<long, int>>();
        ConcurrentDictionary<long, Tuple<long, int>> nodeFinder = new ConcurrentDictionary<long, Tuple<long, int>>();

        //this is blockId, <minNode, maxNode>. Was a mis-assumption from a wrong attempt at thing.
        ConcurrentDictionary<long, Tuple<long, long>> nodeFinder2 = new ConcurrentDictionary<long, Tuple<long, long>>();


        Dictionary<long, long> blockPositions = new Dictionary<long, long>();
        Dictionary<long, int> blockSizes = new Dictionary<long, int>();

        //For tessting purposes, not intended to be actually used as a whole memory store of a file.
        //Dictionary<long, PrimitiveBlock> loadedBlocks = new Dictionary<long, PrimitiveBlock>();
        ConcurrentDictionary<long, PrimitiveBlock> loadedBlocks = new ConcurrentDictionary<long, PrimitiveBlock>();

        //NOTE: If i want to eat more memory directly, i could throw the blocks in a collection and access them directly
        //Or i could fill in all the entries in a block and pull that directly, though this behavior is essentially re-writing
        //OsmSharp as it is and all the speed and RAM issues it hits.
        //My code here might be slower, but it should let me process significantly bigger files when it's completed.

        //methods will need to be a little smarter about stuff existing in RAM.
        //code should run now after indexing from disk. ready to test and get baseline perf info.

        private RuntimeTypeModel _runtimeTypeModel;
        private readonly Type _blockHeaderType = typeof(BlobHeader);
        private readonly Type _blobType = typeof(Blob);
        private readonly Type _primitiveBlockType = typeof(PrimitiveBlock);
        private readonly Type _headerBlockType = typeof(HeaderBlock);

        private PrimitiveBlock _block = new PrimitiveBlock();
        private BlobHeader _header = new BlobHeader();

        //long waysStartAt = 0;
        //long relationsStartAt = 0;
        //long blockCounter = 0;

        //I will use the write lock to make sure threads don't read the wrong data
        //the names will be misleading, since i dont want to use overlapping IO on these even though
        //the docs say I could, since I'd need to Seek() to a position and then read and its possible
        //threads would change the Seek point before the ReadAsync was called.
        System.Threading.ReaderWriterLockSlim msLock = new System.Threading.ReaderWriterLockSlim();

        long currentPosition = 0;
        public void Open(string filename)
        {
            fi = new FileInfo(filename);
            Console.WriteLine(fi.FullName + " | " + fi.Length);
            fs = File.OpenRead(filename);

            _runtimeTypeModel = RuntimeTypeModel.Create();
            _runtimeTypeModel.Add(_blockHeaderType, true);
            _runtimeTypeModel.Add(_blobType, true);
            _runtimeTypeModel.Add(_primitiveBlockType, true);
            _runtimeTypeModel.Add(_headerBlockType, true);

        }

        public void Close()
        {
            fs.Close();
            fs.Dispose();
        }

        public void LoadWholeFile()
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

            //header block left out intentionally, start data blocks at 1
            while (fs.Position != fs.Length)
            {
                blockCounter++;
                bh = Serializer.DeserializeWithLengthPrefix<BlobHeader>(fs, PrefixStyle.Fixed32BigEndian);

                byte[] thisblob = new byte[bh.datasize];
                fs.Read(thisblob, 0, bh.datasize);
                var ms1 = new MemoryStream(thisblob);

                b = Serializer.Deserialize<Blob>(ms1);
                var ms = new MemoryStream(b.zlib_data);
                var dms = new ZlibStream(ms, CompressionMode.Decompress);

                _runtimeTypeModel.Deserialize<PrimitiveBlock>(dms, pb);

                loadedBlocks.TryAdd(blockCounter, pb);
            }
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
                //var ms1 = new MemoryStream(thisblob);

                //b = Serializer.Deserialize<Blob>(ms1);
                //var ms = new MemoryStream(b.zlib_data);
                //var dms = new ZlibStream(ms, CompressionMode.Decompress);

                var passedBC = blockCounter;
                //int groupCounter = 0;
                //_runtimeTypeModel.Deserialize<PrimitiveBlock>(dms, pb);

                //System.Threading.Tasks.Parallel.ForEach(pb.primitivegroup, group =>
                //System.Threading.Tasks.Parallel.For(0, pb.primitivegroup.Count, groupCounter =>
                var tasked = Task.Run(() =>
                {
                    var pb2 = DecodeBlock(thisblob); //Serializer.Deserialize<PrimitiveBlock>(dms);
                    if (pb2.primitivegroup.Count() > 1)
                        Console.WriteLine("This block has " + pb2.primitivegroup.Count() + " groups!");

                    //if (waysStartAt == 0 && group.ways.Count > 0)
                    //  waysStartAt = blockCounter;
                    //if (relationsStartAt == 0 && group.relations.Count > 0)
                    //  relationsStartAt = blockCounter;
                    //var group = pb.primitivegroup[groupCounter];

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


        //NOTE: I might want to keep all existing materialzed blocks in memory for the duration of one operation.
        //That way, I don't have to pull the same block 400 times if it has
        public OsmSharp.Complete.CompleteRelation GetRelation(long relationId, ref ConcurrentDictionary<long, PrimitiveBlock> activeBlocks)
        {
            try
            {
                //Console.WriteLine("getting relation " + relationId);
                //Run after indexing file
                //load only relevant blocks for this entry
                //long relationBlockId = relationValues[relationId];
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
                            //neededBlocks.Add(Int32.Parse(FindBlockKeyForNode(idToFind).Split("-")[0]));
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
                        //SmallNodes.TryAdd(nb, InflateNodes(activeBlocks[nb]));
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
                //TODO: watch for infinite loops here. 

                //add completed members from other functions. load each one once from memory
                //foreach (var id in rel.memids)
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
                                loadedWays.Add(idToFind, GetWay(idToFind, ref activeBlocks));
                            break;
                    }

                }
                //}

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
                            break;
                        case Relation.MemberType.WAY:
                            c.Member = loadedWays[idToFind];
                            break;
                    }
                    crms.Add(c);
                }
                r.Members = crms.ToArray();
                //Console.WriteLine("Relation " + r.Id + " done with" + r.Members.Count() + " entries");

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

        // public OsmSharp.Complete.CompleteWay GetWay(long wayId)
        //{
        //  ConcurrentDictionary<long, PrimitiveBlock> placeholder = new ConcurrentDictionary<long, PrimitiveBlock>();
        // return GetWay(wayId, ref placeholder);
        // }

        public OsmSharp.Complete.CompleteWay GetWay(long wayId, ref ConcurrentDictionary<long, PrimitiveBlock> activeBlocks)
        {
            try
            {
                //Console.WriteLine("getting way " + wayId);
                //Run after indexing file
                //load only relevant blocks for this entry
                //long relationBlockId = relationValues[relationId];
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

                //more deltas 
                long idToFind = 0;
                List<long> neededBlocks = new List<long>();
                for (int i = 0; i < way.refs.Count; i++)
                {
                    idToFind += way.refs[i];
                    neededBlocks.Add(FindBlockKeyForNode(idToFind, ref activeBlocks));
                }

                neededBlocks = neededBlocks.Distinct().ToList(); //I'll also need to fill in any entries from 
                                                                 //Dictionary<long, PrimitiveBlock> activeBlocksinner = new Dictionary<long, PrimitiveBlock>();

                foreach (var nb in neededBlocks)
                {
                    if (!activeBlocks.ContainsKey(nb))
                    {
                        activeBlocks.TryAdd(nb, GetBlock(nb));
                        //SmallNodes.TryAdd(nb, InflateNodes(activeBlocks[nb]));
                    }
                }
                //Now I have the data needed to fill in nodes for a way


                OsmSharp.Complete.CompleteWay finalway = new OsmSharp.Complete.CompleteWay();
                finalway.Id = wayId;
                finalway.Tags = new OsmSharp.Tags.TagsCollection();
                for (int i = 0; i < way.keys.Count(); i++)
                {
                    finalway.Tags.Add(new OsmSharp.Tags.Tag(System.Text.Encoding.UTF8.GetString(wayBlock.stringtable.s[(int)way.keys[i]]), System.Text.Encoding.UTF8.GetString(wayBlock.stringtable.s[(int)way.vals[i]])));
                }

                List<OsmSharp.Node> nodeList = new List<OsmSharp.Node>();
                idToFind = 0;
                foreach (var node in way.refs)
                {
                    idToFind += node;
                    var blockId = FindBlockKeyForNode(idToFind, ref activeBlocks);
                    //if (SmallNodes.ContainsKey(blockId))
                    //  nodeList.Add(GetNodeFromSmallNode(SmallNodes[blockId][idToFind])); 
                    //else
                    nodeList.Add(GetNode(idToFind, ref activeBlocks, true));
                }
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
        public OsmSharp.Node GetNodeFromSmallNode(SmallNode node)
        {
            return new OsmSharp.Node() { Id = node.id, Latitude = node.lat, Longitude = node.lon };
        }

        public OsmSharp.Node GetNode(long nodeId, ref ConcurrentDictionary<long, PrimitiveBlock> activeBlocks, bool skipTags = true)
        {
            //Console.WriteLine("getting node " + nodeId);
            //Run after indexing file
            //load only relevant blocks for this entry
            //long relationBlockId = relationValues[relationId];
            //ConcurrentDictionary<long, Dictionary<long, SmallNode>> SmallNodes = new ConcurrentDictionary<long, Dictionary<long, SmallNode>>(); //fake
            var nodeBlockValues = FindBlockKeyForNode(nodeId, ref activeBlocks);
            //if (nodeBlockValues == null)
            //  throw new Exception("Node not found");


            //var keys = nodeBlockValues.Split("-");
            PrimitiveBlock nodeBlock;
            if (activeBlocks.ContainsKey(nodeBlockValues))
                nodeBlock = activeBlocks[nodeBlockValues];
            else
            {
                nodeBlock = GetBlock(nodeBlockValues);
                activeBlocks.TryAdd(nodeBlockValues, nodeBlock);
                //SmallNodes.TryAdd(nodeBlockValues, InflateNodes(activeBlocks[nodeBlockValues]));
            }
            var nodePrimGroup = nodeBlock.primitivegroup[0];

            long nodeCounter = 0;
            int index = -1;
            while (nodeCounter != nodeId)
            {
                index += 1;
                nodeCounter += nodePrimGroup.dense.id[index];
            }

            OsmSharp.Node filled = new OsmSharp.Node();
            filled.Id = nodeId;
            filled.Latitude = DecodeLatLon(nodePrimGroup.dense.lat[index], nodeBlock.lat_offset, nodeBlock.granularity);
            filled.Longitude = DecodeLatLon(nodePrimGroup.dense.lon[index], nodeBlock.lon_offset, nodeBlock.granularity);

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
                //key is block id - primGroupId
                //value is the tuple list. 1 is min, 2 is max.
                if (nodelist.Value.Item1 > nodeId) //this node's minimum is larger than our node, skip
                    continue;

                if (nodelist.Value.Item2 < nodeId) //this node's maximum is smaller than our node, skip
                    continue;

                //This block can potentially hold the node in question.
                if (!activeBlocks.ContainsKey(nodelist.Key))
                    activeBlocks.TryAdd(nodelist.Key, GetBlock(nodelist.Key));

                var nodeBlock = activeBlocks[nodelist.Key]; //GetBlock(nodelist.Key);

                long nodecounter = 0;
                int nodeIndex = -1;
                //as much as i want to tree search this, the negative delta values really mess that up, since there;s
                //no guarentee nodes are sorted by id
                while (nodeIndex < 7999) //groups can only have 8000 entries.
                {
                    nodeIndex++;
                    nodecounter += nodeBlock.primitivegroup[0].dense.id[nodeIndex];
                    if (nodecounter == nodeId)
                        return nodelist.Key;
                }
            }


            //couldnt find this node
            //return -1;
            throw new Exception("Node Not Found!?!?");

        }

        public void IndexBlock()
        {
            //Do my indexing logic, but only on primGroups in the current block
            //on the assumption that the block holds all necessary data somewhere in it.
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
                            var way = GetWay(w.id, ref activeBlocks);
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

        public List<OsmSharp.Complete.CompleteOsmGeo> GetGeometryFromNextBlockSelfContained()
        {
            //This grabs the last block, populates everything in it to an OsmSharp.Complete object
            //and returns that list. Removes the block from memory once that's done.
            //This is the 'main' function I think i'll use to do most of the work.
            //and doesn't do any lookups from other blocks or anything.
            //Not certain this one will function as expected, but it very well may be what I was actually looking for.

            var lastBlockID = blockPositions.Keys.Max();
            var block = GetBlock(lastBlockID);

            List<OsmSharp.Complete.CompleteOsmGeo> results = new List<OsmSharp.Complete.CompleteOsmGeo>();
            Dictionary<long, OsmSharp.Node> nodeList = new Dictionary<long, OsmSharp.Node>();

            foreach (var primgroup in block.primitivegroup)
            {
                if (primgroup.relations != null && primgroup.relations.Count() > 0)
                {
                    foreach (var r in primgroup.relations)
                    {
                        var relation = GetRelationFromBlock(r, block, nodeList, results);
                        results.Add(relation);
                    }
                }
                else if (primgroup.ways != null && primgroup.ways.Count() > 0)
                {
                    foreach (var w in primgroup.ways)
                    {
                        var way = GetWayFromBlock(w, block, nodeList);
                        results.Add(way);
                    }
                }
                else
                {
                    foreach (var n in primgroup.dense.id)
                    {
                        var node = GetNodeFromBlock(n, block, primgroup);
                        nodeList.Add(n, node);
                    }
                }
            }

            return results;
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

        public static double DecodeLatLon(long valueOffset, long offset, long granularity)
        {
            return .000000001 * (offset + (granularity * valueOffset));
        }
    }
}
