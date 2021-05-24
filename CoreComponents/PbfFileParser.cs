using OsmSharp;
using OsmSharp.Streams;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoreComponents
{
    public static class PbfFileParser
    {
        public static List<long> ProcessFileCoreV4(OsmStreamSource source, bool saveToFile = false, string filename = "")
        {
            // Here, source has already been configured or filtered as needed.
            List<long> processedRelations = new List<long>(); //return this, so the parent function knows what to look for in a full-pass.
            HashSet<long> waysToSkip = new HashSet<long>();

            var relations = source
            .ToComplete()
            .Where(p => p.Type == OsmGeoType.Relation);

            var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            long relationCounter = 0;
            double totalCounter = 0;
            double totalRelations = 0; // relations.Count(); //This causes another pass on the file/stream. Skipping this data for now.
            long totalItems = 0;
            DateTime startedProcess = DateTime.Now;
            TimeSpan difference = DateTime.Now - DateTime.Now;

            Log.WriteLog("Loading relation data into RAM...");
            foreach (var r in relations) //This is where the first memory peak hits as it loads everything into memory
            {
                if (totalCounter == 0)
                {
                    Log.WriteLog("Data loaded.");
                    startedProcess = DateTime.Now;
                    difference = startedProcess - startedProcess;
                }
                totalCounter++;
                try
                {
                    processedRelations.Add(r.Id);
                    var convertedRelation = GeometrySupport.ConvertOsmEntryToStoredElement(r);
                    if (convertedRelation == null)
                    {
                        continue;
                    }

                    if (saveToFile)
                        GeometrySupport.WriteSingleStoredElementToFile(filename, convertedRelation);
                    else
                        db.StoredOsmElements.Add(convertedRelation);

                    totalItems++;
                    relationCounter++;
                    if (relationCounter > 100)
                    {
                        ReportProgress(startedProcess, totalRelations, totalCounter, "Relations");
                        relationCounter = 0;
                    }

                    foreach (var w in ((OsmSharp.Complete.CompleteRelation)r).Members)
                    {
                        if (w.Role == "outer") //Inner ways might have a tag match to apply later.
                            waysToSkip.Add(w.Member.Id);
                    }
                }
                catch (Exception ex)
                {
                    Log.WriteLog("Error Processing Relation " + r.Id + ": " + ex.Message);
                }
            }
            Log.WriteLog("Relations loaded at " + DateTime.Now);

            var ways = source
            .ToComplete()
            .Where(p => p.Type == OsmGeoType.Way && !waysToSkip.Contains(p.Id)); //avoid loading skippable Ways into RAM in the first place.

            double wayCounter = 0;
            double totalWays = 0; // ways.Count(); //skipping additional pass on steam/file.
            totalCounter = 0;
            var fourPercentWays = totalWays / 25;

            Log.WriteLog("Loading Way data into RAM...");
            foreach (var w in ways) //Testing looks like multithreading doesn't provide any improvement here.
            {
                if (totalCounter == 0)
                {
                    Log.WriteLog("Data Loaded");
                    startedProcess = DateTime.Now;
                    difference = DateTime.Now - DateTime.Now;
                }
                totalCounter++;
                try
                {
                    totalItems++;
                    wayCounter++;
                    var item = GeometrySupport.ConvertOsmEntryToStoredElement(w);
                    if (item == null)
                        continue;

                    if (saveToFile)
                        GeometrySupport.WriteSingleStoredElementToFile(filename, item);
                    else
                        db.StoredOsmElements.Add(item);

                    if (wayCounter > 10000)
                    {
                        ReportProgress(startedProcess, totalWays, totalCounter, "Ways");
                        wayCounter = 0;
                    }
                }
                catch (Exception ex)
                {
                    if (w == null)
                        Log.WriteLog("Error Processing Way : Way was null");
                    else
                        Log.WriteLog("Error Processing Way " + w.Id + ": " + ex.Message);
                }
            }

            Log.WriteLog("Ways loaded at " + DateTime.Now);

            var points = source.AsParallel()
            .ToComplete() //unnecessary for nodes, but needed for the converter function.
            .Where(p => p.Type == OsmGeoType.Node && TagParser.getFilteredTags(p.Tags).Count > 0);  //Keep nodes that have tags after removing irrelevant tags.

            double nodeCounter = 0;
            double totalnodes = 0; // points.Count(); third extra pass on things skipped.
            totalCounter = 0;
            Log.WriteLog("Loading Node data into RAM...");
            foreach (OsmSharp.Node p in points)
            {
                if (totalCounter == 0)
                {
                    //Log.WriteLog("Node Data Loaded"); //For nodes, it does actually stream them as it reads the file.
                    startedProcess = DateTime.Now;
                    difference = DateTime.Now - DateTime.Now;
                }
                totalCounter++;
                try
                {
                    totalItems++;
                    nodeCounter++;
                    var item = GeometrySupport.ConvertOsmEntryToStoredElement(p);
                    if (item == null)
                        continue;

                    if (saveToFile)
                        GeometrySupport.WriteSingleStoredElementToFile(filename, item);
                    else
                        db.StoredOsmElements.Add(item);

                    if (nodeCounter > 1000)
                    {
                        ReportProgress(startedProcess, totalnodes, totalCounter, "Nodes");
                        nodeCounter = 0;
                    }
                }
                catch (Exception ex)
                {
                    if (p == null)
                        Log.WriteLog("Error Processing Node: Node was null");
                    else
                        Log.WriteLog("Error Processing Node  " + p.Id + ": " + ex.Message);
                }
            }

            if (!saveToFile)
            {
                Log.WriteLog("Saving " + totalItems + " entries to the database.....");
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                db.SaveChanges();
                sw.Stop();
                Log.WriteLog("Saving complete in " + sw.Elapsed + ".");
            }

            return processedRelations;

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
