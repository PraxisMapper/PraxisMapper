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
        public static List<long> ProcessFileCoreV4(OsmStreamSource source, bool saveToFile = false, string filename = "")
        {
            // Here, source has already been configured or filtered as needed.
            Log.WriteLog("Starting to process elements from PBF file at " + DateTime.Now);
            List<long> processedRelations = new List<long>(); //If we're being used in a multiple-pass case, we need this to know which relations still to process on the last pass.
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
            .Where(p => p.Type == OsmGeoType.Way && !waysToSkip.Contains(p.Id)); //avoid loading skippable Ways into RAM in the first place.
            ProcessInnerLoop(ways, "Way", 100000, true, filename, db);
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

            return processedRelations;
        }

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
                    Log.WriteLog("Error Processing " + itemType + + r.Id + ": " + ex.Message);
                }
            }
            if (saveToFile)
                GeometrySupport.WriteStoredElementListToFile(saveFilename, ref elements);
            else
                db.StoredOsmElements.AddRange(elements);
            elements.Clear();
            Log.WriteLog(itemType + " saved to file at " + DateTime.Now);

            return handledItems;
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
