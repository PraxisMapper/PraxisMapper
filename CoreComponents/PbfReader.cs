﻿using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using OsmSharp.Complete;
using OsmSharp.Geo;
using OsmSharp.IO.PBF;
using OsmSharp.Tags;
using PraxisCore.Support;
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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

//NOTE:
//Don't process planet.osm.pbf for mapTiles. It's too slow. The reason is that unlike the extract files (I source them from geofabrik.de),
//planet.osm uses multiple groups in each of its blocks. This makes the primary chokepoint on reading the file become deserializing blocks
//through protobuf code. Especially in worst cases, where it's got to load a whole block of nearly 100,000 points for one of them, multiple times.
//Until them, planet.osm should be saved for smaller styleSets, like adminBounds(filled) or suggestedGameplay.

//NOTE: 
//An attempt to write directly to offline format was made here. It was removed, as it is dramatically slower than loading OfflinePlaces and writing them out from there.
//(The cost of indexing is far, far lower than the cost of sorting and writing each zip file every time it's edited)
namespace PraxisCore.PbfReader
{
    public record struct IndexInfo(int blockId, int groupId, byte groupType, long minId, long maxId); //trying this as a struct. 22 bytes instead of 16 as suggested, but about 4% faster.
    /// <summary>
    /// PraxisMapper's customized, multithreaded PBF parser. Saves on RAM usage by relying on disk access when needed. Can resume a previous session if stopped for some reason.
    /// </summary>
    public class PbfReader
    {
        //The 7th generation of logic for pulling geometry out of a pbf file. This one is written specfically for PraxisMapper, and
        //doesn't depend on OsmSharp for reading the raw data now. OsmSharp's still used for object types, but we have our own
        //FundamentalOsm class for holding data to pass to a new FeatureInterpreter

        static readonly int initialCapacity = 8009; //ConcurrentDictionary says initial capacity shouldn't be divisible by a small prime number, so i picked the prime closes to 8,000 for initial capacity
        static readonly int initialConcurrency = Environment.ProcessorCount;

        public bool saveToDB = true;
        //public bool onlyMatchedAreas = false; //if true, only process geometry if the tags come back with IsGamplayElement== true;
        public string processingMode = "normal"; //normal: use geometry as it exists. center: save the center point of any geometry provided instead of its actual value. minimize: reduce accuracy to save storage space. offline: data is only going to be used to make separate files, not run in a live server.
        public string styleSet = "mapTiles"; //which style set to use when parsing entries
        public string[] styleSets = ["mapTiles"];
        public bool keepIndexFiles = true;
        public bool splitByStyleSet = false;

        public string outputPath = "";
        public string filenameHeader = "";

        public bool lowResourceMode = false;
        //public bool reprocessFile = false; //if true, we load TSV data from a previous run and re-process that by the rules.
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
        public Polygon importBounds = null; //If set, we're only importing data that fits inside this polygon.

        ConcurrentDictionary<long, PrimitiveBlock> activeBlocks = new ConcurrentDictionary<long, PrimitiveBlock>(initialConcurrency, initialCapacity);
        ConcurrentDictionary<long, bool> accessedBlocks = new ConcurrentDictionary<long, bool>(initialConcurrency, initialCapacity);

        int nextBlockId = 0;
        long firstWayBlock = 0;
        int startNodeBtreeIndex = 0;
        int startWayBtreeIndex = 0;
        int startRelationBtreeIndex = 0;

        int nodeIndexEntries = 0;
        int wayIndexEntries = 0;
        int relationIndexEntries = 0;

        ConcurrentBag<Task> relList = new ConcurrentBag<Task>(); //Individual, smaller tasks.
        ConcurrentBag<TimeSpan> timeListRelations = new ConcurrentBag<TimeSpan>(); //how long each Group took to process.
        ConcurrentBag<TimeSpan> timeListWays = new ConcurrentBag<TimeSpan>(); //how long each Group took to process.
        ConcurrentBag<TimeSpan> timeListNodes = new ConcurrentBag<TimeSpan>(); //how long each Group took to process.

        long nodeGroupsProcessed = 0;
        long totalProcessEntries = 0;

        public bool displayStatus = true;

        CancellationTokenSource tokensource = new CancellationTokenSource();
        CancellationToken token;

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

        public void ProcessFileV2(string filename, long relationId = 0)
        {
            //Most of this might be handlable by updating ProcessReaderResults and a few smaller tweaks to the ProcessFile loop?

            //This one starts from the start and works it ways towards the end, one block/group at a time, directly against the database.
            //NOTE: styleSet is used to determine what counts as matched/unmatched, but matched elements are pre-tagged against all style sets.

            //Don't do this. We hit OOM before this can catch up and clear memory pressure.
            //GCSettings.LatencyMode = GCLatencyMode.LowLatency;

            var dbS = new PraxisContext();
            var skipExisting = !dbS.Places.Any();
            dbS.Dispose();

            styleSets = styleSet.Split(",");
            Stopwatch swFile = new Stopwatch();
            swFile.Start();

            PrepareFile(filename);
            var Bcount = BlockCount();
            if (nextBlockId == Bcount - 1)
                nextBlockId = 1; //0 is the header.

            if (relationId != 0)
            {
                filenameHeader += relationId.ToString() + "-";
                //Get the source relation first
                var relation = MakeCompleteRelation(relationId);
                var NTSrelation = GeometrySupport.ConvertOsmEntryToPlace(relation, styleSet);
                bounds = NTSrelation.ElementGeometry.EnvelopeInternal;
            }
            else if (importBounds != null)
                bounds = importBounds.EnvelopeInternal;

            //Moved these out of the foreach loop so we dont declare them new every pass.
            var sw = Stopwatch.StartNew();
            int thisBlockId = 0;
            long groupMin = 0;
            long groupMax = 0;
            PrimitiveGroup group;
            int changed = 0;
            ConcurrentBag<FundamentalOsm> geoData;
            List<long> placeIds;
            PrimitiveBlock blockData;
            TagsCollection tempTags = new TagsCollection();
            for (var block = nextBlockId; block < Bcount; block++)
            {
                blockData = GetBlock(block, false);

                int nextGroup = 0;
                int itemType = 0;
                if (block < firstWayBlock)
                    itemType = 1;
                else if (block < relationIndex[0].blockId)
                    itemType = 2;
                else
                    itemType = 3;

                thisBlockId = block;
                for (int groupId = nextGroup; groupId < blockData.primitivegroup.Count; groupId++)
                {
                    sw.Restart();
                    group = blockData.primitivegroup[groupId];
                    if (CanSkipGroup(itemType, group, blockData.stringtable.s))
                    {
                        Log.WriteLog("Skipped matchless block " + block + " group " + groupId + " in " + sw.Elapsed.ToString());
                        continue;
                    }

                    switch (itemType)
                    {
                        case 1:
                            //Nodes require the whole block to process, so we will process the whole block at once for those, and skip the later groups
                            groupMin = nodeIndex.Where(n => n.blockId == block).Min(n => n.minId);
                            groupMax = nodeIndex.Where(n => n.blockId == block).Max(n => n.maxId);
                            break;
                        case 2:
                            groupMin = wayIndex.Where(n => n.blockId == block && n.groupId == groupId).First().minId;
                            groupMax = wayIndex.Where(n => n.blockId == block && n.groupId == groupId).First().maxId;
                            break;
                        case 3:
                            groupMin = relationIndex.Where(n => n.blockId == block && n.groupId == groupId).First().minId;
                            groupMax = relationIndex.Where(n => n.blockId == block && n.groupId == groupId).First().maxId;
                            break;
                    }

                    geoData = GetGeometryFromGroupAsCoords(thisBlockId, group, true);
                    changed = 0;
                    //There are large relation blocks where you can see how much time is spent writing them or waiting for one entry to
                    //process as the apps drops to a single thread in use, but I can't do much about those if I want to be able to resume a process.
                    if (geoData != null && !geoData.IsEmpty) //This process function is sufficiently parallel that I don't want to throw it off to a Task. The only sequential part is writing the data to the file, and I need that to keep accurate track of which blocks have beeen written to the file.
                    {
                        placeIds = geoData.Where(g => g != null).Select(g => g.entryId).ToList();

                        if (placeIds.Count == 0)
                        {
                            Log.WriteLog("Processed a group but got no results in " + sw.Elapsed.ToString());
                            continue;
                        }

                        if (processingMode == "offline")
                        {
                            //It's 5-10x faster to process these straight to the OfflinePlace format than to covert Place to OfflinePlace.
                            List<DbTables.OfflinePlace> processedO = geoData.AsParallel().Select(g =>
                            {
                                if (g == null)
                                    return null;
                                var place = GeometrySupport.ConvertFundamentalOsmToOfflinePlace(g, styleSet);
                                if (processingMode == "center")
                                    place.ElementGeometry = place.ElementGeometry.Centroid;
                                else if (processingMode == "expandPoints")
                                {
                                    if (place.SourceItemType == 1)
                                        place.ElementGeometry = place.ElementGeometry.Buffer(ConstantValues.resolutionCell8 / 2); //TODO: cell10 resolution?
                                }

                                return place;
                            }
                            ).Where(p => p != null).ToList();

                            using var db = new PraxisContext(); //create a new one each group to free up RAM faster
                            db.ChangeTracker.AutoDetectChangesEnabled = false; //automatic change tracking disabled for performance, we only track the stuff we actually change.
                            db.Database.SetCommandTimeout(600);
                            db.OfflinePlaces.AddRange(processedO);
                            try
                            {
                                changed = db.SaveChanges();
                            }
                            catch (Exception ex)
                            {
                                Log.WriteLog("Error saving Block " + block + " Group " + groupId + ": " + ex.Message);
                                //TODO: save this in a recoverable way and apply it back to the DB later.
                                //This occasionally throws an error about a named double somewhere, that makes no sense to me.
                                var jsonData = JsonSerializer.Serialize(processedO);
                                File.WriteAllText("block-" + block + "-group-" + groupId + ".json", jsonData);
                            }
                        }
                        else
                        {
                            List<DbTables.Place> processed = geoData.AsParallel().Select(g =>
                            {
                                if (g == null)
                                    return null;
                                var place = GeometrySupport.ConvertFundamentalOsmToPlace(g, styleSet);

                                if (processingMode == "center")
                                    place.ElementGeometry = place.ElementGeometry.Centroid;
                                else if (processingMode == "expandPoints")
                                {
                                    if (place.SourceItemType == 1)
                                        place.ElementGeometry = place.ElementGeometry.Buffer(ConstantValues.resolutionCell8 / 2); //TODO: cell10 resolution?
                                }

                                return place;
                            }).Where(p => p != null).ToList();

                            if (!saveToDB)
                            {
                                //NOTE: this writes each group to its own file, because updating a single existing file causes a full rewrite of the
                                //file at this time. Writing separate files keeps the app running fast.

                                //Skip post-processing when writing to disk, let databases handle that.
                                var pe = new PlaceExport(filename.Replace(".pbf", "-" + block + "-" + groupId + ".pmd"));
                                changed = processed.Count;
                                foreach (var place in processed)
                                    pe.AddEntry(place);

                                try
                                {
                                    pe.Close();
                                }
                                catch (Exception ex)
                                {
                                    Log.WriteLog("Error writing data to disk:" + ex.Message, Log.VerbosityLevels.Errors);
                                }
                            }
                            else
                            {
                                using var db = new PraxisContext(); //create a new one each group to free up RAM faster
                                db.ChangeTracker.AutoDetectChangesEnabled = false; //automatic change tracking disabled for performance, we only track the stuff we actually change.
                                db.Database.SetCommandTimeout(600);
                                if (processingMode == "offline")
                                {
                                    var entries = processed.Select(p => DbTables.OfflinePlace.FromPlace(p, styleSet));
                                    db.OfflinePlaces.AddRange(entries);
                                }
                                else
                                {
                                    Dictionary<long, DbTables.Place> currentData = null;
                                    if (!skipExisting)
                                        currentData = db.Places.Include(p => p.PlaceData).Where(p => p.SourceItemType == itemType && placeIds.Contains(p.SourceItemID)).ToDictionary(k => k.SourceItemID, v => v);
                                    foreach (var newEntry in processed)
                                    {
                                        if (newEntry == null)
                                            continue;

                                        if (skipExisting || !currentData.TryGetValue(newEntry.SourceItemID, out var existing))
                                            db.Places.Add(newEntry);
                                        else
                                        {
                                            Place.UpdateChanges(existing, newEntry, db);
                                        }
                                    }
                                }

                                try
                                {
                                    changed = db.SaveChanges(); //This count includes tags and placedata.
                                }
                                catch (Exception ex)
                                {
                                    //TODO: more precise error handling and potentially in-line recovery
                                    //"max_allowed_packet" error may be fixable by splitting up the write commands or passing a command to MariaDB
                                    var message = ex.Message;
                                    while (ex.InnerException != null)
                                    {
                                        ex = ex.InnerException;
                                        message = ex.Message;
                                    }
                                    Log.WriteLog("Error saving Block " + block + " Group " + groupId + ": " + message);

                                    //NOTE: copy/pasting save to file here as a way to quick-recover this data later.
                                    //TOOD: functionalize this later.
                                    var pe2 = new PlaceExport(filename.Replace(".pbf", "-" + block + "-" + groupId + ".pmd"));
                                    changed = geoData.Count;
                                    foreach (var place in processed)
                                        pe2.AddEntry(place);

                                    try
                                    {
                                        pe2.Close();
                                    }
                                    catch (Exception ex2)
                                    {
                                        Log.WriteLog("Error writing data to disk:" + ex2.Message, Log.VerbosityLevels.Errors);
                                    }
                                }
                                db.Dispose();
                            }
                        }
                    }
                    geoData = null;
                    SaveCurrentBlockAndGroup(block, groupId);
                    sw.Stop();
                    Log.WriteLog("Block " + block + " Group " + groupId + ": " + changed + (itemType == 1 ? " Node" : itemType == 2 ? " Way" : " Relation") + " and Tag entries modified in " + sw.Elapsed);

                    if (itemType == 3)
                        timeListRelations.Add(sw.Elapsed);
                    else if (itemType == 2)
                        timeListWays.Add(sw.Elapsed);
                    else
                    {
                        timeListNodes.Add(sw.Elapsed);
                        break; //skip all later groups for nodes, they're all handled at once.
                    }
                }
                blockData = null;
                nextBlockId++;
            }

            //TODO: If this run created any PMD files because a block failed to be added to the database, this is where we'll try again.
            //may also need to increase packet size, as thats the most common error.

            //NOTE: this may not be appropriate for the PBFReader, since there could be mulitple files to run, or PMD files to process
            //This generally belongs at a higher level in the process, not here.
            //Create index check
            var indexCheck2 = new PraxisContext();
            var makeIndexes = indexCheck2.GlobalData.FirstOrDefault(d => d.DataKey == "createIndexes");
            if (makeIndexes != null && makeIndexes.DataValue.ToUTF8String() == "pending")
            {
                indexCheck2.RecreateIndexes();
                makeIndexes.DataValue = "completed".ToByteArrayUTF8();
                indexCheck2.SaveChanges();
            }
            //Expiring map tiles upon completion
            Log.WriteLog("Expiring all map tiles");
            indexCheck2.ExpireAllMapTiles();
            indexCheck2.ExpireAllSlippyMapTiles();
            indexCheck2.Dispose();
            indexCheck2 = null;

            Close();
            CleanupFiles();
            swFile.Stop();
            Log.WriteLog(filename + " completed at " + DateTime.Now + ", session lasted " + swFile.Elapsed);
        }

        public bool CanSkipGroup(int itemType, PrimitiveGroup group, List<byte[]> strings)
        {
            var tempTags = new TagsCollection(10);
            if (itemType == 1) //Nodes do this slightly differently than other groups.
            {
                if (group.dense.keys_vals.Count == group.dense.id.Count)
                    return true;

                var entryCounter = 0;
                for (int i = 0; i < group.dense.keys_vals.Count; i++)
                {
                    if (group.dense.keys_vals[i] == 0)
                    {
                        if (tempTags.Count > 0)
                        {
                            if (EntryMatchesSet(tempTags))
                            {
                                return false;
                            }
                            tempTags.Clear();
                        }

                        entryCounter++; //skip to next entry.
                        continue;
                    }

                    var tag = new Tag(
                            Encoding.UTF8.GetString(strings[group.dense.keys_vals[i++]]), //i++ returns i, but increments the value.
                            Encoding.UTF8.GetString(strings[group.dense.keys_vals[i]])
                        );
                    tempTags.Add(tag);
                }
                return true;
            }
            else if (itemType == 2)
            {
                for (int i = 0; i < group.ways.Count; i++)
                {
                    for (int k = 0; k < group.ways[i].keys.Count; k++)
                    {
                        var tag = new Tag(
                            Encoding.UTF8.GetString(strings[(int)group.ways[i].keys[k]]), //i++ returns i, but increments the value.
                            Encoding.UTF8.GetString(strings[(int)group.ways[i].vals[k]])
                        );
                        tempTags.Add(tag);
                    }
                    if (EntryMatchesSet(tempTags))
                    {
                        return false;
                    }
                    tempTags.Clear();
                }
                return true;
                
            }
            else if (itemType == 3)
            {
                for (int i = 0; i < group.relations.Count; i++)
                {
                    for (int k = 0; k < group.relations[i].keys.Count; k++)
                    {
                        var tag = new Tag(
                            Encoding.UTF8.GetString(strings[(int)group.relations[i].keys[k]]), //i++ returns i, but increments the value.
                            Encoding.UTF8.GetString(strings[(int)group.relations[i].vals[k]])
                        );
                        tempTags.Add(tag);
                    }
                    if (EntryMatchesSet(tempTags))
                    {
                        return false;
                    }
                    tempTags.Clear();
                }
                return true;
            }
            return false;
        }

        public void TrySaveToDb(PraxisContext db)
        {
            //TODO: functionalize this bit
        }

        public void DeleteMissingItems(string filename)
        {
            //TODO: deleting items from the existing DB is harder than add/update
            //The user might have picked a particular style set, or multiple ones, or multiple files
            //Therefore, delete is its own pass with its own rules.
            //Delete must be done with a file that encompasses ALL the areas previously loaded
            //(EX: if you loaded NY and NJ state files, you have to use at least the US Northeast extract)
            //Delete can be fast because it ONLY needs to see if the ID exists in the PBF file,
            //so I don't have to process the actual geometry. 

            //index helps? 
            //check DB for IDs between start and end per block
            //If any in DB are missing from file, delete them
            //check for values between blocks, if any are in db delete them.
            PrepareFile(filename);
            var Bcount = BlockCount();

            var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            for (var block = nextBlockId; block < Bcount; block++)
            {
                var blockData = GetBlock(block);
                for (int groupId = 0; groupId < blockData.primitivegroup.Count; groupId++)
                {
                    var group = blockData.primitivegroup[groupId];
                    IndexInfo index = new IndexInfo();
                    int itemType = 0;
                    if (block < firstWayBlock)
                        itemType = 1;
                    else if (block < relationIndex[0].blockId)
                        itemType = 2;
                    else
                        itemType = 3;

                    switch (itemType)
                    {
                        case 1:
                            index = nodeIndex.First(n => n.blockId == block && n.groupId == groupId);
                            break;
                        case 2:
                            index = wayIndex.First(n => n.blockId == block && n.groupId == groupId);
                            break;
                        case 3:
                            index = relationIndex.First(n => n.blockId == block && n.groupId == groupId);
                            break;
                    }

                    //TODO: work out correct rules for checking items BETWEEN blocks/groups. This logic will work on planet.osm.pbf, but may not on extracts.
                    var dbEntries = db.Places.Where(p => p.SourceItemType == itemType && (p.SourceItemID >= index.minId && p.SourceItemID <= index.maxId)).ToList();
                    foreach (var e in dbEntries)
                    {
                        bool isPresent = false;
                        switch (itemType)
                        {
                            case 1:
                                //Nodes need to be scanned, probably shouldnt be done incrementally in a foreach.
                                //if (group.)
                                break;
                            case 2:
                                isPresent = (group.ways.FirstOrDefault(w => w.id == e.SourceItemID) == null);
                                if (!isPresent)
                                    db.Places.Remove(e);
                                break;
                            case 3:
                                isPresent = (group.relations.FirstOrDefault(r => r.id == e.SourceItemID) == null);
                                if (!isPresent)
                                    db.Places.Remove(e);
                                break;
                        }
                    }
                    db.SaveChanges();
                }
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
                        geoListOfOne.Add(MakeCompleteRelation(relId.id, true, thisBlock));
                        ProcessReaderResults(geoListOfOne, block, i);
                        activeBlocks.Clear();
                        geoListOfOne.Clear();
                    }
                }
                else if (group.ways.Count > 0)
                {
                    foreach (var wayId in group.ways)
                    {
                        geoListOfOne.Add(MakeCompleteWay(wayId.id, -1, true));
                        ProcessReaderResults(geoListOfOne, block, i);
                        activeBlocks.Clear();
                        geoListOfOne.Clear();
                    }
                }
                else if (group.nodes.Count > 0)
                {
                    var nodes = GetTaggedNodesFromBlock(thisBlock, true);
                    ProcessReaderResults((IEnumerable<CompleteOsmGeo>)nodes, block, i);
                }
                SaveCurrentBlockAndGroup(block, i);
            }
        }

        public void ProcessAllNodeBlocks(long maxNodeBlock)
        {
            //Throw each node block into its own thread.
            //Parallel.For(1, maxNodeBlock, (block) => //parallel here dies on planet.osm.pbf. Moved to make groups parallel.
            for (int b = 1; b < maxNodeBlock; b++)
            {
                var blockData = GetBlock(b);
                var geoData = GetTaggedNodesFromBlock(blockData, true);
                blockData = null;
                if (geoData != null)
                {
                    totalProcessEntries += geoData.Count;
                    ProcessReaderResults(geoData, b, 0);
                }

                activeBlocks.TryRemove(b, out _);
            }
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
                fs.ReadExactly(thisblob, 0, bh.datasize);

                var passedBC = blockCounter;
                //NOTE: this might be a good place to look at variable capture instead of letting thisblock get captured by
                //the lambda itself. Perhaps this is a good candidate for an action.
                var tasked = Task.Run(() =>  //Threading makes this run approx. twice as fast.
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

            Task.WaitAll(waiting);

            //Now, we should save the IndexInfo list to disk and sort it into sub-indexes
            var indexList = indexInfos.OrderBy(i => i.blockId).ThenBy(i => i.groupId).ToList();
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

        private static void SplitIndexData(List<IndexInfo> indexes)
        {
            nodeIndex = indexes.Where(i => i.groupType == 1).OrderBy(i => i.minId).ToList();
            wayIndex = indexes.Where(i => i.groupType == 2).OrderBy(i => i.minId).ToList();
            relationIndex = indexes.Where(i => i.groupType == 3).OrderBy(i => i.minId).ToList();

            Log.WriteLog("File has " + relationIndex.Count + " relation groups, " + wayIndex.Count + " way groups, and " + nodeIndex.Count + " node groups");
        }

        /// <summary>
        /// If a block is already in memory, returns it. If it isn't, load it from disk and add it to memory.
        /// </summary>
        /// <param name="blockId">the ID for the block in question</param>
        /// <returns>the PrimitiveBlock requested</returns>
        public PrimitiveBlock GetBlock(long blockId, bool persistInRam = true)
        {
            //Track that this entry was requested for this processing block.
            //If the block is in memory, return it.
            //If not, load it from disk and return it.
            PrimitiveBlock results;
            if (!activeBlocks.TryGetValue(blockId, out results))
            {
                //get lock, and check again.
                SimpleLockable.PerformWithLock(blockId.ToString(), () =>
                {
                    //check again, other thread may have acquired the data while we waited on a lock
                    if (!activeBlocks.TryGetValue(blockId, out results))
                    {
                        results = GetBlockFromFile(blockId);
                        if (persistInRam)
                            activeBlocks.TryAdd(blockId, results);
                        accessedBlocks.TryAdd(blockId, true);
                    }
                });
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
            long pos1 = blockPositions[blockId];
            int size1 = blockSizes[blockId];
            byte[] thisblob1 = new byte[size1];

            using (var fs = File.OpenRead(fi.FullName))
            {
                fs.Seek(pos1, SeekOrigin.Begin);
                fs.ReadExactly(thisblob1, 0, size1);
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
            dms2.Close(); dms2.Dispose(); ms3.Close(); ms3.Dispose(); ms2.Close(); ms2.Dispose();
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

        static TagsCollection GetTags(List<byte[]> stringTable, List<uint> keys, List<uint> vals)
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
        public CompleteRelation MakeCompleteRelation(long relationId, bool ignoreUnmatched = false, PrimitiveBlock relationBlock = null)
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
                    string roleType = Encoding.UTF8.GetString(relationBlock.stringtable.s[role]);
                    if (roleType == "inner" || roleType == "outer")
                    {
                        canProcess = true; //I need at least one outer, and inners require outers.
                        break;
                    }
                }

                if (!canProcess)
                    return null;


                TagsCollection tags = GetTags(stringData, rel.keys, rel.vals);
                //TODO: test and move this logic to allow PBFReader to handle multiple style sets at once.
                if (ignoreUnmatched)
                {
                    if (!EntryMatchesSet(tags))
                        return null; //This is 'unmatched' by all our style sets, skip processing this entry.
                }

                //If I only want elements that show up in the map, and exclude areas I don't currently match,
                //I have to knows my tags BEFORE doing the rest of the processing.
                CompleteRelation r = new CompleteRelation();
                r.Id = relationId;
                r.Tags = tags;

                int capacity = rel.memids.Count;
                r.Members = new CompleteRelationMember[capacity];
                int hint = -1;

                bool inBounds = false;
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
                            var way = MakeCompleteWay(idToFind, hint, false, true);
                            c.Member = way;
                            if (!inBounds)
                                if (bounds == null || (way != null && way.Nodes.Any(n => bounds.MaxY >= n.Latitude && bounds.MinY <= n.Latitude && bounds.MaxX >= n.Longitude && bounds.MinX <= n.Longitude)))
                                    inBounds = true;
                            break;
                        case Relation.MemberType.RELATION: //ignore meta-relations
                            break;
                    }
                    r.Members[i] = c;
                }

                if (!inBounds)
                    return null;

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
        private CompleteWay MakeCompleteWay(long wayId, int hint = -1, bool ignoreUnmatched = false, bool skipTags = false) //skipTags is for relations, where the way's tags don't matter.
        {
            try
            {
                var wayBlockValues = FindBlockInfoForWay(wayId, out var position, hint);

                PrimitiveBlock wayBlock = GetBlock(wayBlockValues.blockId);
                var wayPrimGroup = wayBlock.primitivegroup[wayBlockValues.groupId];
                var way = FindWayInPrimGroup(wayPrimGroup.ways, wayId);
                if (way == null)
                    return null; //way wasn't in the block it was supposed to be in.

                return MakeCompleteWay(way, wayBlock.stringtable.s, ignoreUnmatched, skipTags);
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
        private CompleteWay MakeCompleteWay(Way way, List<byte[]> stringTable, bool ignoreUnmatched = false, bool skipTags = false)
        {
            try
            {
                TagsCollection tags = null;
                //We always need to apply tags here, so we can either skip after (if IgnoredUmatched is set) or to pass along tag values correctly.
                if (!skipTags)
                {
                    tags = GetTags(stringTable, way.keys, way.vals);

                    if (ignoreUnmatched)
                    {
                        if (!EntryMatchesSet(tags))
                            return null; //This is 'unmatched' by all our style sets, skip processing this entry.
                    }
                }

                CompleteWay finalway = new CompleteWay();
                finalway.Id = way.id;
                finalway.Tags = tags;

                //NOTES:
                //This gets all the entries we want from each node, then loads those all in 1 pass per referenced block/group.
                //This is significantly faster than doing a GetBlock per node when 1 block has mulitple entries
                //its a little complicated but a solid performance boost.
                int entryCount = way.refs.Count;
                long idToFind = 0; //more deltas 
                Dictionary<int, IndexInfo> nodeInfoEntries = new Dictionary<int, IndexInfo>(entryCount); //hint(position in array), IndexInfo
                int hint = -1; //last result for previous node.
                Dictionary<long, int> nodesByIndexInfo = new Dictionary<long, int>(entryCount); //nodeId, hint(Position in array)

                for (int i = 0; i < entryCount; i++)
                {
                    idToFind += way.refs[i];
                    var blockInfo = FindBlockInfoForNode(idToFind, out var index, hint);
                    hint = index;
                    nodeInfoEntries.TryAdd(index, blockInfo);
                    nodesByIndexInfo.TryAdd(idToFind, index);
                }
                var nodesByBlockGroup = nodesByIndexInfo.ToLookup(k => k.Value, v => v.Key); //hint(Position in array), nodeIDs

                finalway.Nodes = new OsmSharp.Node[entryCount];
                //Each way is already in its own thread, I think making each of those fire off 1 thread per node block referenced is excessive.
                //and may well hurt performance in most cases due to overhead and locking the concurrentdictionary than it gains.
                Dictionary<long, OsmSharp.Node> AllNodes = new Dictionary<long, OsmSharp.Node>(entryCount);
                foreach (var block in nodesByBlockGroup)
                {
                    var someNodes = GetAllNeededNodesInBlockGroup(nodeInfoEntries[block.Key], block.OrderBy(b => b).ToArray());
                    foreach (var n in someNodes)
                        AllNodes.Add(n.Key, n.Value);
                }

                //Optimization: If any of these nodes are inside our bounds, continue. If ALL of them are outside, skip processing this entry.
                //This can result in a weird edge case where an object has a LINE that goes through our boundaries, but we ignore it because no NODES are in-bounds.
                if (bounds != null) //we have bounds, and we aren't loading this as part of a relation, so lets make sure it's in bounds.
                    if (!AllNodes.Any(n => bounds.MaxY >= n.Value.Latitude && bounds.MinY <= n.Value.Latitude && bounds.MaxX >= n.Value.Longitude && bounds.MinX <= n.Value.Longitude))
                        return null;

                //Iterate over the list of referenced Nodes again, but this time to assign created nodes to the final results.
                idToFind = 0;
                for (int i = 0; i < entryCount; i++)
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
        private List<OsmSharp.Node> GetTaggedNodesFromBlock(PrimitiveBlock block, bool ignoreUnmatched = false)
        {
            List<OsmSharp.Node> taggedNodes = new List<OsmSharp.Node>(block.primitivegroup.Sum(p => p.dense.keys_vals.Count) - block.primitivegroup.Sum(p => p.dense.id.Count) / 2); //precise count
            for (int b = 0; b < block.primitivegroup.Count; b++)
            {
                nodeGroupsProcessed++;
                var dense = block.primitivegroup[b].dense;

                //Shortcut: if dense.keys.count == dense.id.count, there's no tagged nodes at all here (0 means 'no keys', and all 0's means every entry has no keys)
                if (dense.keys_vals.Count == dense.id.Count)
                    continue;

                var granularity = block.granularity;
                var lat_offset = block.lat_offset;
                var lon_offset = block.lon_offset;
                var stringData = block.stringtable.s;

                //sort out tags ahead of time.
                int entryCounter = 0;
                List<Tuple<int, Tag>> idKeyVal = new List<Tuple<int, Tag>>((dense.keys_vals.Count - dense.id.Count) / 2);
                for (int i = 0; i < dense.keys_vals.Count; i++)
                {
                    if (dense.keys_vals[i] == 0)
                    {
                        entryCounter++; //skip to next entry.
                        continue;
                    }

                    idKeyVal.Add(
                        Tuple.Create(entryCounter,
                        new Tag(
                            Encoding.UTF8.GetString(stringData[dense.keys_vals[i++]]), //i++ returns i, but increments the value.
                            Encoding.UTF8.GetString(stringData[dense.keys_vals[i]])
                        )
                    ));
                }
                var decodedTags = idKeyVal.ToLookup(k => k.Item1, v => v.Item2);
                var lastTaggedNode = entryCounter;

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

                    var tc = new OsmSharp.Tags.TagsCollection(decodedTags[index]); //Tags are needed first, so pull them out here for the ignoreUnmatched check.
                    if (ignoreUnmatched)
                    {
                        if (!EntryMatchesSet(tc))
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
            }
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
            int lastCurrent;
            while (min != max)
            {
                var check = nodeIndex[current];
                if (check.minId > nodeId) //this ways minimum is larger than our way, shift maxs down
                    max = current;
                else if (check.maxId < nodeId) //this ways maximum is smaller than our way, shift min up.
                    min = current;
                else
                    return check;

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
            int lastCurrent;
            while (min != max)
            {
                var check = wayIndex[current];
                if (check.minId > wayId) //this ways minimum is larger than our way, shift maxs down
                    max = current;
                else if (check.maxId < wayId) //this ways maximum is smaller than our way, shift min up.
                    min = current;
                else
                    return check;

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

            int lastCurrent;
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
                if (primgroup.relations != null && primgroup.relations.Count > 0)
                {
                    //Some relation blocks can hit 22GB of RAM on their own. Low-resource machines will fail, and should roll into the LastChance path automatically.
                    foreach (var r in primgroup.relations)
                        relList.Add(Task.Run(() => { var rel = MakeCompleteRelation(r.id, onlyTagMatchedEntries, block); results.Add(rel); totalProcessEntries++; }));
                }
                else if (primgroup.ways != null && primgroup.ways.Count > 0)
                {
                    foreach (var r in primgroup.ways)
                    {
                        relList.Add(Task.Run(() => { results.Add(MakeCompleteWay(r, block.stringtable.s, onlyTagMatchedEntries)); totalProcessEntries++; }));
                    }
                }
                else
                {
                    //Useful node lists are so small, they lose performance from splitting each step into 1 task per entry.
                    //Inline all that here as one task
                    relList.Add(Task.Run(() =>
                    {
                        try
                        {
                            var nodes = GetTaggedNodesFromBlock(block, onlyTagMatchedEntries);
                            if (nodes != null)
                            {
                                totalProcessEntries += nodes.Count;
                                results = new ConcurrentBag<ICompleteOsmGeo>(nodes);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.WriteLog("Processing node failed: " + ex.Message, Log.VerbosityLevels.Errors);
                        }
                    }));
                }

                Task.WaitAll(relList);

                //Moved this logic here to free up RAM by removing blocks once we're done reading data from the hard drive. Should result in fewer errors at the ProcessReaderResults step.
                //Slightly more complex: only remove blocks we didn't access last call. saves some serialization effort. Small RAM trade for 30% speed increase.
                if (!keepAllBlocksInRam)
                    activeBlocks.TryRemove(blockId, out _); //We dont need the current one.
                foreach (var blockRead in activeBlocks)
                {
                    if (!accessedBlocks.ContainsKey(blockRead.Key))
                        activeBlocks.TryRemove(blockRead.Key, out _);
                }
                accessedBlocks.Clear();
                return results;
            }
            catch (Exception ex)
            {
                Log.WriteLog("Error getting geometry: " + ex.Message, Log.VerbosityLevels.Errors);
                throw; //In order to reprocess this block in last-chance mode.
                       //return null;
            }
        }

        public ConcurrentBag<FundamentalOsm> GetGeometryFromGroupAsCoords(int blockId, PrimitiveGroup primgroup, bool onlyTagMatchedEntries = false)
        {
            //This grabs the chosen block, populates everything in it to an OsmSharp.Complete object and returns that list
            ConcurrentBag<FundamentalOsm> results = new ConcurrentBag<FundamentalOsm>();
            try
            {
                //Attempting to clear up some memory slightly faster, but this should be redundant.
                relList.Clear();
                var block = GetBlock(blockId);
                if (primgroup.relations != null && primgroup.relations.Count > 0)
                {
                    //Some relation blocks can hit 22GB of RAM on their own. Low-resource machines will fail, and should roll into the LastChance path automatically.
                    foreach (var r in primgroup.relations)
                        relList.Add(Task.Run(() => { var rel = MakeCompleteRelationAsCoords(r.id, onlyTagMatchedEntries, block); if (rel != null) { results.Add(rel); totalProcessEntries++; } }));
                }
                else if (primgroup.ways != null && primgroup.ways.Count > 0)
                {
                    foreach (var r in primgroup.ways)
                    {
                        relList.Add(Task.Run(() => { var way = MakeCompleteWayCoords(r, block.stringtable.s, onlyTagMatchedEntries); if (way != null) { results.Add(way); totalProcessEntries++; } }));
                    }
                }
                else
                {
                    //Useful node lists are so small, they lose performance from splitting each step into 1 task per entry.
                    //Inline all that here as one task
                    relList.Add(Task.Run(() =>
                    {
                        try
                        {
                            var nodes = GetTaggedNodesFromBlockAsCoords(block, onlyTagMatchedEntries);
                            if (nodes != null)
                            {
                                totalProcessEntries += nodes.Count;
                                results = new ConcurrentBag<FundamentalOsm>(nodes); //.Select(n => new FundamentalOsm([[n.GetCoordinate()]], null, new TagsCollection(n.Tags), 1, n.Id.Value)));
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.WriteLog("Processing node failed: " + ex.Message, Log.VerbosityLevels.Errors);
                        }
                    }));
                }

                Task.WaitAll(relList);

                //Moved this logic here to free up RAM by removing blocks once we're done reading data from the hard drive. Should result in fewer errors at the ProcessReaderResults step.
                //Slightly more complex: only remove blocks we didn't access last call. saves some serialization effort. Small RAM trade for 30% speed increase.
                if (!keepAllBlocksInRam)
                    activeBlocks.TryRemove(blockId, out _); //We dont need the current one.
                foreach (var blockRead in activeBlocks)
                {
                    if (!accessedBlocks.ContainsKey(blockRead.Key))
                        activeBlocks.TryRemove(blockRead.Key, out _);
                }
                accessedBlocks.Clear();
                return results;
            }
            catch (Exception ex)
            {
                Log.WriteLog("Error getting geometry: " + ex.Message, Log.VerbosityLevels.Errors);
                throw; //In order to reprocess this block in last-chance mode.
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
        private static double DecodeLatLon(long valueOffset, long blockOffset, long blockGranularity) //TODO: good candidate for [MethodImpl(MethodImplOptions.AggressiveInlining)]?
        {
            return .000000001 * (blockOffset + (blockGranularity * valueOffset));
        }
        //end OsmSharp copied functions.

        private void SaveIndexInfo(List<IndexInfo> index)
        {
            try
            {
                string filename = outputPath + fi.Name + ".indexinfo";
                string[] data = new string[index.Count];
                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = index[i].blockId + ":" + index[i].groupId + ":" + index[i].groupType + ":" + index[i].minId + ":" + index[i].maxId;
                }
                File.WriteAllLines(filename, data);
            }
            catch
            {
                //Log.WriteLog("Failed on SaveIndexInfo: " + ex.Message + ex.StackTrace);
                //We probably got called from a folder we don't have write permisisons into.
            }
        }

        /// <summary>
        /// Saves the block positions created for the currently opened file to their own files, so that they can be read instead of created if processing needs to resume later.
        /// </summary>
        private void SaveBlockInfo()
        {
            try
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
            catch
            {
                //Log.WriteLog("Failed on SaveBlockInfo: " + ex.Message + ex.StackTrace);
                //We probably got called from a folder we don't have write permisisons into.
            }
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
            catch
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
            try
            {
                string filename = outputPath + fi.Name + ".progress";
                File.WriteAllTextAsync(filename, blockID.ToString() + ":" + groupId);
            }
            catch
            {
                //Log.WriteLog("Failed on SaveCurrentBlockAndGroupInfo: " + ex.Message + ex.StackTrace);
                //We probably got called from a folder we don't have write permisisons into.
            }
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
            catch
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
            catch
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
                    var relationGroupsLeft = relationIndex.Count(w => w.blockId > nextBlockId);
                    var wayGroupsLeft = wayIndex.Count(w => w.blockId > nextBlockId);
                    var nodeGroupsLeft = nodeIndex.Count(w => w.blockId > nextBlockId);
                    var groupsDone = timeListRelations.Count + timeListWays.Count + nodeGroupsProcessed;
                    Log.WriteLog("Current stats (" + DateTime.Now.ToString() + "):");
                    Log.WriteLog("Groups completed this run: " + groupsDone);
                    Log.WriteLog("Remaining groups to process: " + relationGroupsLeft + " relations, " + wayGroupsLeft + " ways, " + nodeGroupsLeft + " nodes");
                    Log.WriteLog("Processing tasks: " + relList.Count(r => !r.IsCompleted));
                    Log.WriteLog("Total Processed Entries: " + totalProcessEntries);

                    if (groupsDone > 0)
                    {
                        string group = "";
                        double time = 0.0;
                        if (!timeListRelations.IsEmpty)
                        {
                            group = "Relation";
                            time = timeListRelations.Average(t => t.TotalSeconds) * relationGroupsLeft;
                        }
                        else if (!timeListWays.IsEmpty)
                        {
                            group = "Way";
                            time = timeListWays.Average(t => t.TotalSeconds) * wayGroupsLeft;
                        }
                        else
                        {
                            group = "Node";
                            time = timeListNodes.Average(t => t.TotalSeconds) * nodeGroupsLeft;
                        }
                        Log.WriteLog("Time to complete " + group + " groups: " + new TimeSpan((long)time * 10000000));
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
        public int ProcessReaderResults(IEnumerable<ICompleteOsmGeo> items, long blockId, int groupId)
        {
            if (items == null || !items.Any())
                return 0;

            //This one is easy, we just dump the geodata to the file.
            int actualCount = 0;
            string saveFilename = outputPath + Path.GetFileNameWithoutExtension(fi.Name) + "-" + blockId + "-" + groupId;
            ConcurrentBag<DbTables.Place> elements = new ConcurrentBag<DbTables.Place>();
            relList.Clear();
            foreach (var r in items)
            {
                if (r != null)
                    relList.Add(Task.Run(() => { var e = GeometrySupport.ConvertOsmEntryToPlace(r, styleSet); if (e != null) elements.Add(e); }));
            }
            Task.WaitAll(relList);

            if (elements.IsEmpty)
                return 0;

            if (processingMode == "center")
                foreach (var e in elements)
                    e.ElementGeometry = e.ElementGeometry.Centroid;

            actualCount = elements.Count;
            if (saveToDB) //If this is on, we skip the file-writing part and send this data directly to the DB. Single threaded, but doesn't waste disk space with intermediate files.
            {
                using var db = new PraxisContext();
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                db.Places.AddRange(elements);
                db.SaveChanges();
                return actualCount;
            }
            else if (splitByStyleSet) //TODO: This block is only accessible via the original processfile. Restore this functionality somehow?
            {
                saveFilename = outputPath + Path.GetFileNameWithoutExtension(fi.Name) + "-";

                PlaceExport pe = new PlaceExport(saveFilename + ".pmd");
                Dictionary<string, PlaceExport> exportsByMatch = new Dictionary<string, PlaceExport>();
                foreach (var s in TagParser.allStyleGroups[styleSet])
                {
                    exportsByMatch.Add(s.Value.Name, new PlaceExport(saveFilename + s.Value.Name + ".pmd"));
                }
                //Save to separate files based on which style set entry each one matched up to.
                foreach (var md in elements)
                {
                    var areatype = TagParser.GetStyleEntry(md, styleSet).Name;
                    exportsByMatch[areatype].AddEntry(md);
                }

                foreach (var dataSet in exportsByMatch)
                    if (dataSet.Value.totalEntries > 0)
                        dataSet.Value.WriteToDisk();
            }
            else
            {
                PlaceExport pe = new PlaceExport(outputPath + Path.GetFileNameWithoutExtension(fi.Name) + ".pmd");
                foreach (var md in elements)
                    pe.AddEntry(md);

                try
                {
                    pe.Close();
                    //pe.WriteToDisk(); //TODO: thread/queue/async?
                }
                catch (Exception ex)
                {
                    Log.WriteLog("Error writing data to disk:" + ex.Message, Log.VerbosityLevels.Errors);
                }
            }

            return actualCount; //some invalid options were passed and we didnt run through anything.
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
            Log.WriteLog("Starting to load one way from file.");
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
            nextBlockId = 1;
            if (relationIndex.Count == 0)
            {
                IndexFile();
                SetOptimizationValues();
                SaveBlockInfo();
            }
            else
            {
                var lastBlock = FindLastCompletedBlock();
                if (lastBlock == -1)
                {
                    nextBlockId = 1;
                    SaveCurrentBlockAndGroup(nextBlockId, 0);
                }
                else
                    nextBlockId = lastBlock + 1;
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
                        Log.WriteLog("Force-dumping half of block cache to minimize swap-file thrashing at " + DateTime.Now.ToString());
                        foreach (var block in activeBlocks)
                            if (Random.Shared.Next() % 2 == 0)
                                activeBlocks.TryRemove(block.Key, out _);

                        Log.WriteLog("Percentage of runtime spent in GC: " + data.PauseTimePercentage.ToString());
                        GC.Collect();
                        Log.WriteLog("Force-dump and GC completed at " + DateTime.Now.ToString());
                    }
                    Thread.Sleep(30000);
                }
            }, token);
        }

        public static void QueueWriteTask(string filename, StringBuilder data)
        {
            SimpleLockable.PerformWithLock(filename, () => //AsTask still occasionally doesnt work? No idea why.
            {
                data.Length = data.Length - 2; //ignore final /r/n characters so we don't have a blank line.
                var file = File.OpenWrite(filename);
                file.Position = file.Length;
                StreamWriter sw = new StreamWriter(file);
                sw.Write(data.ToString());
                sw.Close(); sw.Dispose(); file.Close(); file.Dispose();
            });
        }

        public bool EntryMatchesSet(TagsCollectionBase tags)
        {
            foreach (var set in styleSets)
            {
                if (TagParser.GetStyleEntry(tags, set).Name != TagParser.defaultStyle.Name)
                    return true;
            }
            return false;
        }

        //Using Coordinate arrays is very slightly faster than CompleteOsmGeo items,
        //but I want all the performance I can get on huge files.
        public FundamentalOsm LoadOneWayAsCoordsFromFile(string filename, long wayId)
        {
            Log.WriteLog("Starting to load one way from file as coords.");
            try
            {
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                PrepareFile(filename);

                var way = MakeCompleteWayAsCoords(wayId, ignoreUnmatched: false);
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

        private List<FundamentalOsm> GetTaggedNodesFromBlockAsCoords(PrimitiveBlock block, bool ignoreUnmatched = false)
        {
            List<FundamentalOsm> taggedNodes = new List<FundamentalOsm>(block.primitivegroup.Sum(p => p.dense.keys_vals.Count) - block.primitivegroup.Sum(p => p.dense.id.Count) / 2); //precise count
            for (int b = 0; b < block.primitivegroup.Count; b++)
            {
                nodeGroupsProcessed++;
                var dense = block.primitivegroup[b].dense;

                //Shortcut: if dense.keys.count == dense.id.count, there's no tagged nodes at all here (0 means 'no keys', and all 0's means every entry has no keys)
                if (dense.keys_vals.Count == dense.id.Count)
                    continue;

                var granularity = block.granularity;
                var lat_offset = block.lat_offset;
                var lon_offset = block.lon_offset;
                var stringData = block.stringtable.s;

                //sort out tags ahead of time.
                int entryCounter = 0;
                List<Tuple<int, Tag>> idKeyVal = new List<Tuple<int, Tag>>((dense.keys_vals.Count - dense.id.Count) / 2);
                for (int i = 0; i < dense.keys_vals.Count; i++)
                {
                    if (dense.keys_vals[i] == 0)
                    {
                        entryCounter++; //skip to next entry.
                        continue;
                    }

                    idKeyVal.Add(
                        Tuple.Create(entryCounter,
                        new Tag(
                            Encoding.UTF8.GetString(stringData[dense.keys_vals[i++]]), //i++ returns i, but increments the value.
                            Encoding.UTF8.GetString(stringData[dense.keys_vals[i]])
                        )
                    ));
                }
                var decodedTags = idKeyVal.ToLookup(k => k.Item1, v => v.Item2);
                var lastTaggedNode = entryCounter;

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

                    var tc = new OsmSharp.Tags.TagsCollection(decodedTags[index]); //Tags are needed first, so pull them out here for the ignoreUnmatched check.
                    if (ignoreUnmatched)
                    {
                        if (!EntryMatchesSet(tc))
                            continue;
                    }

                    Coordinate c = new Coordinate(DecodeLatLon(lon, lon_offset, granularity), DecodeLatLon(lat, lat_offset, granularity));
                    FundamentalOsm n = new FundamentalOsm([[c]], null, tc, 1, nodeId);

                    //if bounds checking, drop nodes that aren't needed.
                    if (bounds == null || (c.Y >= bounds.MinY && c.Y <= bounds.MaxY && c.X >= bounds.MinX && c.X <= bounds.MaxX))
                        taggedNodes.Add(n);

                    if (index >= lastTaggedNode)
                        break;
                }
            }
            return taggedNodes;
        }

        private FundamentalOsm MakeCompleteWayCoords(Way way, List<byte[]> stringTable, bool ignoreUnmatched = false, bool skipTags = false)
        {
            try
            {
                TagsCollection tags = null;
                //We always need to apply tags here, so we can either skip after (if IgnoredUmatched is set) or to pass along tag values correctly.
                if (!skipTags)
                {
                    tags = GetTags(stringTable, way.keys, way.vals);

                    if (ignoreUnmatched)
                    {
                        if (!EntryMatchesSet(tags))
                            return null; //This is 'unmatched' by all our style sets, skip processing this entry.
                    }
                }

                CompleteWay finalway = new CompleteWay();
                finalway.Id = way.id;
                finalway.Tags = tags;

                //NOTES:
                //This gets all the entries we want from each node, then loads those all in 1 pass per referenced block/group.
                //This is significantly faster than doing a GetBlock per node when 1 block has mulitple entries
                //its a little complicated but a solid performance boost.
                int entryCount = way.refs.Count;
                long idToFind = 0; //more deltas 
                Dictionary<int, IndexInfo> nodeInfoEntries = new Dictionary<int, IndexInfo>(entryCount); //hint(position in array), IndexInfo
                int hint = -1; //last result for previous node.
                Dictionary<long, int> nodesByIndexInfo = new Dictionary<long, int>(entryCount); //nodeId, hint(Position in array)

                for (int i = 0; i < entryCount; i++)
                {
                    idToFind += way.refs[i];
                    var blockInfo = FindBlockInfoForNode(idToFind, out var index, hint);
                    hint = index;
                    nodeInfoEntries.TryAdd(index, blockInfo);
                    nodesByIndexInfo.TryAdd(idToFind, index);
                }
                var nodesByBlockGroup = nodesByIndexInfo.ToLookup(k => k.Value, v => v.Key); //hint(Position in array), nodeIDs

                finalway.Nodes = new OsmSharp.Node[entryCount];
                Dictionary<long, Coordinate> AllNodes = new Dictionary<long, Coordinate>(entryCount);
                foreach (var block in nodesByBlockGroup)
                {
                    var someNodes = GetAllNeededNodesInBlockGroup(nodeInfoEntries[block.Key], block.OrderBy(b => b).ToArray());
                    foreach (var n in someNodes)
                        AllNodes.Add(n.Key, n.Value.GetCoordinate());
                }

                //Optimization: If any of these nodes are inside our bounds, continue. If ALL of them are outside, skip processing this entry.
                //This can result in a weird edge case where an object has a LINE that goes through our boundaries, but we ignore it because no NODES are in-bounds.
                if (bounds != null) //we have bounds, and we aren't loading this as part of a relation, so lets make sure it's in bounds.
                    if (!AllNodes.Any(n => bounds.MaxY >= n.Value.Y && bounds.MinY <= n.Value.Y && bounds.MaxX >= n.Value.X && bounds.MinX <= n.Value.X))
                        return null;

                //Iterate over the list of referenced Nodes again, but this time to assign created nodes to the final results.
                Coordinate[] c = new Coordinate[way.refs.Count];
                idToFind = 0;
                for (int i = 0; i < entryCount; i++)
                {
                    idToFind += way.refs[i]; //delta coding.
                    c[i] = AllNodes[idToFind];
                }

                var result = new FundamentalOsm([c], null, tags, 2, way.id);

                return result;
            }
            catch (Exception ex)
            {
                Log.WriteLog("MakeCompleteWayAsCoords failed: " + ex.Message + ex.StackTrace, Log.VerbosityLevels.Errors);
                return null; //Failed to get way, probably because a node didn't exist in the file.
            }
        }

        private FundamentalOsm MakeCompleteWayAsCoords(long wayId, int hint = -1, bool ignoreUnmatched = false, bool skipTags = false) //skipTags is for relations, where the way's tags don't matter.
        {
            try
            {
                var wayBlockValues = FindBlockInfoForWay(wayId, out var position, hint);

                PrimitiveBlock wayBlock = GetBlock(wayBlockValues.blockId);
                var wayPrimGroup = wayBlock.primitivegroup[wayBlockValues.groupId];
                var way = FindWayInPrimGroup(wayPrimGroup.ways, wayId);
                if (way == null)
                    return null; //way wasn't in the block it was supposed to be in.

                return MakeCompleteWayCoords(way, wayBlock.stringtable.s, ignoreUnmatched, skipTags);
            }
            catch (Exception ex)
            {
                Log.WriteLog("MakeCompleteWay failed: " + ex.Message + ex.StackTrace, Log.VerbosityLevels.Errors);
                return null; //Failed to get way, probably because a node didn't exist in the file.
            }
        }

        public FundamentalOsm LoadOneRelationFromFileAsCoords(string filename, long relationId)
        {
            Log.WriteLog("Starting to load one relation as coords from file.");
            try
            {
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                PrepareFile(filename);

                var relation = MakeCompleteRelationAsCoords(relationId);
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

        public FundamentalOsm MakeCompleteRelationAsCoords(long relationId, bool ignoreUnmatched = false, PrimitiveBlock relationBlock = null)
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
                    string roleType = Encoding.UTF8.GetString(stringData[role]);
                    if (roleType == "inner" || roleType == "outer")
                    {
                        canProcess = true; //I need at least one outer, and inners require outers.
                        break;
                    }
                }

                if (!canProcess)
                    return null;

                TagsCollection tags = GetTags(stringData, rel.keys, rel.vals);
                //TODO: test and move this logic to allow PBFReader to handle multiple style sets at once.
                if (ignoreUnmatched)
                {
                    if (!EntryMatchesSet(tags))
                        return null; //This is 'unmatched' by all our style sets, skip processing this entry.
                }

                List<Coordinate[]> outers = new List<Coordinate[]>();
                List<Coordinate[]> inners = new List<Coordinate[]>();

                int capacity = rel.memids.Count;
                int hint = -1;

                bool inBounds = false;
                long idToFind = 0;
                for (int i = 0; i < capacity; i++)
                {
                    idToFind += rel.memids[i]; //memIds is delta-encoded
                    if (rel.types[i] != Relation.MemberType.WAY)
                        continue;
                    var role = Encoding.UTF8.GetString(stringData[rel.roles_sid[i]]);
                    if (role != "inner" && role != "outer")
                        continue;

                    var wayKey = FindBlockInfoForWay(idToFind, out int indexPosition, hint);
                    hint = indexPosition;
                    var way = MakeCompleteWayAsCoords(idToFind, hint, false, true);
                    if (way == null)
                        return null;

                    if (role == "inner")
                        inners.Add(way.outers[0]);
                    else
                        outers.Add(way.outers[0]);

                    if (!inBounds)
                        if (bounds == null || (way != null && way.outers[0].Any(n => bounds.MaxY >= n.Y && bounds.MinY <= n.Y && bounds.MaxX >= n.X && bounds.MinX <= n.X)))
                            inBounds = true;
                }

                if (!inBounds)
                    return null;

                FundamentalOsm results = new FundamentalOsm(outers.ToArray(), inners.ToArray(), tags, 3, rel.id);

                //Some memory cleanup slightly early, in an attempt to free up RAM faster.
                rel = null;
                return results;
            }
            catch (Exception ex)
            {
                Log.WriteLog("relation failed:" + ex.Message, Log.VerbosityLevels.Errors);
                return null;
            }
        }

        //future todo, may not make it into the next release yet.
        public string[] FindAllStyleMatches()
        {
            //This should loop through styleSets, and return the first match's set and style.
            //Assuming that items won't get matched across mulitple style sets.
            return [];
        }
    }
}