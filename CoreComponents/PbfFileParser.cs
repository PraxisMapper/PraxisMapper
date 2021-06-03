using CoreComponents.Support;
using OsmSharp;
using OsmSharp.Streams;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CoreComponents.DbTables;


namespace CoreComponents
{
    public static class PbfFileParser
    {
        public static ProcessResults ProcessFileCoreV4(OsmStreamSource source, bool saveToFile = false, string filename = "")
        {
            //TODO: should this return a class (record) with a list of relations AND ways processed? That would let us avoid duplicates for sure.
            // Here, source has already been configured or filtered as needed.
            Log.WriteLog("Starting to process elements from PBF file at " + DateTime.Now);
            List<long> processedRelations = new List<long>(); //If we're being used in a multiple-pass case, we need this to know which relations still to process on the last pass.
            List<long> processedWays = new List<long>(); //If we're being used in a multiple-pass case, we need this to know which relations still to process on the last pass.
            HashSet<long> waysToSkip = new HashSet<long>();
            var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            var relations = source
            .ToComplete()
            .Where(p => p.Type == OsmGeoType.Relation);
            processedRelations = ProcessInnerLoop(relations, "Relation", 1000, saveToFile, filename, db, waysToSkip);
            Log.WriteLog("Relations saved to file at " + DateTime.Now + ". Processing Ways...");

            var ways = source
            .ToComplete()
            .Where(p => p.Type == OsmGeoType.Way && !waysToSkip.Contains(p.Id) && TagParser.getFilteredTags(p.Tags).Count > 0); //avoid loading skippable Ways into RAM in the first place. Also don't load untagged ways, if there are any
            //Though, adding the TagParser call might cause more RAM to be used, which is a bit of a surprise. That should use less by excluding more areas?
            processedWays = ProcessInnerLoop(ways, "Way", 100000, true, filename, db);
            Log.WriteLog("Ways loaded at " + DateTime.Now);

            var points = source.AsParallel()
            .ToComplete() //unnecessary for nodes, but needed for the converter function.
            .Where(p => p.Type == OsmGeoType.Node && TagParser.getFilteredTags(p.Tags).Count > 0);  //Keep nodes that have tags after removing irrelevant tags.
            ProcessInnerLoop(points, "Node", 10000, saveToFile, filename, db);

            Log.WriteLog("Processing completed at " + DateTime.Now);
            if (!saveToFile)
            {
                Log.WriteLog("Saving all entries to the database.....");
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                db.SaveChanges();
                sw.Stop();
                Log.WriteLog("Saving complete in " + sw.Elapsed + ".");
            }

            return new ProcessResults(processedRelations, processedWays);
        }

        static System.Threading.ReaderWriterLockSlim everythingLock = new System.Threading.ReaderWriterLockSlim();
        public static void ProcessEverything(OsmStreamSource source, string filename = "")
        {
            //TODO: alternative, experimental idea.
            //instead of doing 3 passes (1 per OSM type), take each (complete) object and kick it off to a Task<> that converts the element
            //to my stored type, and writes it to the file itself (acquiring the needed lock, of course)
            //This is obviously slower, since each item needs to do a write to a file.
            //This taught me that the CompleteOsm filestream isn't a very good stream, since it has to keep everything in memory when I use complete objects.

            foreach (var thing in source.ToComplete())
                Task.Run(() => ProcessSingleEntry(thing, filename));
        }

        public static void ProcessSingleEntry(OsmSharp.Complete.ICompleteOsmGeo element, string filename)
        {
            var convertedItem = GeometrySupport.ConvertOsmEntryToStoredElement(element);
            if (convertedItem != null)
            {
                everythingLock.EnterWriteLock();
                GeometrySupport.WriteSingleStoredElementToFile(filename, convertedItem);
                everythingLock.ExitWriteLock();
                if (element.Type == OsmGeoType.Relation)
                    element = null; //hopefully frees up a little RAM. Ways and Nodes might get reused.
            }
        }

        //This doesn;t look like it's going to work. The stream is going to load all elements, and then process skip/take values.
        //public static List<long> ProcessFileCoreV4SkipTake(OsmStreamSource source, bool saveToFile = false, string filename = "")
        //{
        //    // Here, source has already been configured or filtered as needed.
        //    int skip = 0;
        //    int take = 500; //500 relations should be ok.
        //    Log.WriteLog("Starting to skip-take process elements from PBF file at " + DateTime.Now);
        //    List<long> processedRelations = new List<long>(); //If we're being used in a multiple-pass case, we need this to know which relations still to process on the last pass.
        //    HashSet<long> waysToSkip = new HashSet<long>();
        //    var db = new PraxisContext();
        //    db.ChangeTracker.AutoDetectChangesEnabled = false;

        //    bool loopAgain = true;
        //    while (loopAgain)
        //    {
        //        Log.WriteLog("Starting relation pass...");
        //        var relations = source
        //        .ToComplete()
        //        .Where(p => p.Type == OsmGeoType.Relation)
        //        .Skip(skip)
        //        .Take(take);
        //        var resultCount = relations.Count(); //nope, this still fully populates the stream.
        //        skip += resultCount;
        //        if (resultCount < take)
        //            loopAgain = false;

        //        processedRelations.AddRange(ProcessInnerLoop(relations, "Relation", 1000, saveToFile, filename, db, waysToSkip));
        //    }
        //    Log.WriteLog("Relations saved to file at " + DateTime.Now + ". Processing Ways...");

        //    skip = 0;
        //    take = 100000; //100k ways should be ok.
        //    loopAgain = true;
        //    while (loopAgain)
        //    {
        //        Log.WriteLog("Starting Way pass...");
        //        var ways = source
        //        .ToComplete()
        //        .Skip(skip)
        //        .Take(take)
        //        .Where(p => p.Type == OsmGeoType.Way && !waysToSkip.Contains(p.Id)); //avoid loading skippable Ways into RAM in the first place.

        //        var resultCount = ways.Count();
        //        skip += resultCount;
        //        if (resultCount < take)
        //            loopAgain = false;
        //        ProcessInnerLoop(ways, "Way", 100000, true, filename, db);
        //    }
        //    Log.WriteLog("Ways loaded at " + DateTime.Now);

        //    while (loopAgain)
        //    {
        //        Log.WriteLog("Starting Node pass...");
        //        var points = source.AsParallel()
        //        .ToComplete() //unnecessary for nodes, but needed for the converter function.
        //        .Where(p => p.Type == OsmGeoType.Node && TagParser.getFilteredTags(p.Tags).Count > 0)  //Keep nodes that have tags after removing irrelevant tags.
        //        .Skip(skip)
        //        .Take(take);

        //        var resultCount = points.Count();
        //        skip += resultCount;
        //        if (resultCount < take)
        //            loopAgain = false;
        //        ProcessInnerLoop(points, "Node", 10000, saveToFile, filename, db);
        //    }

        //    Log.WriteLog("Processing completed at " + DateTime.Now);
        //    if (!saveToFile)
        //    {
        //        Log.WriteLog("Saving all entries to the database.....");
        //        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        //        sw.Start();
        //        db.SaveChanges();
        //        sw.Stop();
        //        Log.WriteLog("Saving complete in " + sw.Elapsed + ".");
        //    }

        //    return processedRelations;
        //}

        public static List<long> ProcessInnerLoop(IEnumerable<OsmSharp.Complete.ICompleteOsmGeo> items, string itemType, int itemsPerLoop, bool saveToFile = false, string saveFilename = "", PraxisContext db = null, HashSet<long> waysToSkip = null)
        {
            //We want to return both of these lists, dont we? or if we've run the whole file, we only need the waysToSkip one?
            List<long> handledItems = new List<long>();
            List<StoredOsmElement> elements = new List<StoredOsmElement>();
            Log.WriteLog("Loading " + itemType + " data into RAM...");
            long totalCounter = 0;
            long totalItems = 0;
            long itemCounter = 0;
            DateTime startedProcess = DateTime.Now;
            TimeSpan difference;
            foreach (var r in items) //This is where the first memory peak hits as it loads everything into memory
            //Parallel.ForEach(items, r =>
            {
                if (totalCounter == 0)
                {
                    Log.WriteLog(itemType + " Data loaded.");
                    startedProcess = DateTime.Now;
                    difference = startedProcess - startedProcess;
                }
                totalCounter++;
                try
                {
                    handledItems.Add(r.Id);
                    //Note: Lake Superior has 4000+ members in its relation.
                    Log.WriteLog("Converting " + itemType + " " + r.Id, Log.VerbosityLevels.High); //for when individual elements fail to convert, identify the last one we tried.
                    var convertedItem = GeometrySupport.ConvertOsmEntryToStoredElement(r);
                    if (convertedItem == null)
                    {
                        continue;
                        //return null;
                    }
                    elements.Add(convertedItem);
                    totalItems++;
                    itemCounter++;
                    if (itemCounter > itemsPerLoop)
                    {
                        if (saveToFile)
                            GeometrySupport.WriteStoredElementListToFile(saveFilename, ref elements);
                        else
                            db.StoredOsmElements.AddRange(elements);

                        ReportProgress(startedProcess, 0, totalCounter, itemType);
                        itemCounter = 0;
                        elements.Clear();
                    }

                    if (itemType == "Relation")
                    {
                        foreach (var w in ((OsmSharp.Complete.CompleteRelation)r).Members)
                        {
                            if (w.Role == "outer") //Inner ways might have a tag match to apply later.
                                waysToSkip.Add(w.Member.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteLog("Error Processing " + itemType + +r.Id + ": " + ex.Message);
                }
            }
            //);
            if (saveToFile)
                GeometrySupport.WriteStoredElementListToFile(saveFilename, ref elements);
            else
                db.StoredOsmElements.AddRange(elements);
            elements.Clear();
            Log.WriteLog(itemType + " saved to file at " + DateTime.Now);

            return handledItems;
        }

        public static List<long> ProcessInnerLoopParallel(IEnumerable<OsmSharp.Complete.ICompleteOsmGeo> items, string itemType, int itemsPerLoop, bool saveToFile = false, string saveFilename = "", PraxisContext db = null, HashSet<long> waysToSkip = null)
        {
            //We want to return both of these lists, dont we? or if we've run the whole file, we only need the waysToSkip one?
            List<long> handledItems = new List<long>();
            List<StoredOsmElement> elements = new List<StoredOsmElement>();
            Log.WriteLog("Loading " + itemType + " data into RAM...");
            long totalCounter = 0;
            long totalItems = 0;
            long itemCounter = 0;
            DateTime startedProcess = DateTime.Now;
            TimeSpan difference;
            System.Threading.ReaderWriterLockSlim loopLock = new System.Threading.ReaderWriterLockSlim();
            //foreach (var r in items) //This is where the first memory peak hits as it loads everything into memory
            Parallel.ForEach(items, r =>
            {
                if (totalCounter == 0)
                {
                    Log.WriteLog(itemType + " Data loaded.");
                    startedProcess = DateTime.Now;
                    difference = startedProcess - startedProcess;
                }
                totalCounter++;
                try
                {
                    handledItems.Add(r.Id);
                    //Note: Lake Superior has 4000+ members in its relation.
                    Log.WriteLog("Converting " + itemType + " " + r.Id, Log.VerbosityLevels.High); //for when individual elements fail to convert, identify the last one we tried.
                    //var convertedItem = GeometrySupport.ConvertOsmEntryToStoredElement(r);
                    var convertedItem = Task.Run(() => GeometrySupport.ConvertOsmEntryToStoredElement(r)).Result;
                    if (convertedItem == null)
                    {
                        return;
                    }
                    elements.Add(convertedItem);
                    totalItems++;
                    itemCounter++;
                    loopLock.EnterWriteLock();
                    if (itemCounter > itemsPerLoop)
                    {
                        if (saveToFile)
                            GeometrySupport.WriteStoredElementListToFile(saveFilename, ref elements);
                        else
                            db.StoredOsmElements.AddRange(elements);

                        ReportProgress(startedProcess, 0, totalCounter, itemType);
                        itemCounter = 0;
                        elements.Clear();
                    }
                    loopLock.ExitWriteLock();

                    if (itemType == "Relation")
                    {
                        foreach (var w in ((OsmSharp.Complete.CompleteRelation)r).Members)
                        {
                            if (w.Role == "outer") //Inner ways might have a tag match to apply later.
                                waysToSkip.Add(w.Member.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteLog("Error Processing " + itemType + +r.Id + ": " + ex.Message);
                }
            }
            );

            if (saveToFile)
                GeometrySupport.WriteStoredElementListToFile(saveFilename, ref elements);
            else
                db.StoredOsmElements.AddRange(elements);
            elements.Clear();
            Log.WriteLog(itemType + " saved to file at " + DateTime.Now);

            return handledItems;
        }



        //For small files, possibly for filtered boxes on an area.
        //Could allow for self-contained mobile games to get their DB created without setting up a db and web server.
        public static List<StoredOsmElement> ProcessSkipDatabase(IEnumerable<OsmSharp.Complete.ICompleteOsmGeo> items, int itemsPerLoop)
        {
            List<StoredOsmElement> elements = new List<StoredOsmElement>();
            Log.WriteLog("Loading All OSM elements into RAM...");
            long totalCounter = 0;
            long totalItems = 0;
            long itemCounter = 0;
            DateTime startedProcess = DateTime.Now;
            TimeSpan difference;
            foreach (var r in items) //This is where the first memory peak hits as it loads everything into memory
            {
                if (totalCounter == 0)
                {
                    Log.WriteLog("PBF Data loaded.");
                    startedProcess = DateTime.Now;
                    difference = startedProcess - startedProcess;
                }
                totalCounter++;
                try
                {
                    var convertedItem = GeometrySupport.ConvertOsmEntryToStoredElement(r);
                    if (convertedItem == null)
                    {
                        continue;
                    }
                    elements.Add(convertedItem);
                    totalItems++;
                    itemCounter++;
                    if (itemCounter > itemsPerLoop)
                    {
                        ReportProgress(startedProcess, 0, totalCounter, "PBF elements");
                        itemCounter = 0;
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteLog("Error Processing PBF element " + r.Id + ": " + ex.Message);
                }
            }

            Log.WriteLog("PBF entries converted and held in RAM at " + DateTime.Now);

            return elements;
        }

        public static void ReportProgress(DateTime startedProcess, double totalItems, double itemsProcessed, string itemName)
        {
            var difference = DateTime.Now - startedProcess;
            var entriesPerSecond = itemsProcessed / difference.TotalSeconds;

            if (totalItems > 0)
            {
                double percentage = (itemsProcessed / totalItems) * 100;
                var secondsLeft = (totalItems - itemsProcessed) / entriesPerSecond;
                TimeSpan estimatedTime = TimeSpan.FromSeconds(secondsLeft);
                Log.WriteLog(Math.Round(entriesPerSecond) + " " + itemName + " processed per second, " + Math.Round(percentage, 2) + "% done, estimated time remaining: " + estimatedTime.ToString());
            }
            else
                Log.WriteLog(Math.Round(entriesPerSecond) + " " + itemName + " processed per second.");
        }
    }
}
