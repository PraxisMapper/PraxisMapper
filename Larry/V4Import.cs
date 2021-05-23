using CoreComponents;
using OsmSharp;
using OsmSharp.Streams;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static CoreComponents.DbTables;
using static CoreComponents.Singletons;
using static CoreComponents.TagParser;
using CoreComponents.Support;
using NetTopologySuite.Geometries;

namespace Larry
{
    public static class V4Import
    {
        //Several of these files should probably be moved to CoreComponents. 
        //Processing the files, at least ProcessFileCoreV4 should probably be part of the CoreComponents dll
        //The save/load to external file functions should be moved too.

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
                    var convertedRelation = GeometrySupport.ConvertOsmEntryToStoredWay(r);
                    if (convertedRelation == null)
                    {
                        continue;
                    }

                    if (saveToFile)
                        GeometrySupport.WriteSingleStoredWayToFile(filename, convertedRelation);
                    else
                        db.StoredWays.Add(convertedRelation);

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
                    var item = GeometrySupport.ConvertOsmEntryToStoredWay(w);
                    if (item == null)
                        continue;

                    if (saveToFile)
                        GeometrySupport.WriteSingleStoredWayToFile(filename, item);
                    else
                        db.StoredWays.Add(item);

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

            var defaultColor = SKColor.Parse(defaultTagParserEntries.Last().HtmlColorCode);
            var points = source.AsParallel()
            .ToComplete() //unnecessary for nodes, but needed for the converter function.
            .Where(p => p.Type == OsmGeoType.Node && p.Tags.Count > 0 && GetStyleForOsmWay(p.Tags).paint.Color != defaultColor);
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
                    var item = GeometrySupport.ConvertOsmEntryToStoredWay(p);
                    if (item == null)
                        continue;

                    if (saveToFile)
                        GeometrySupport.WriteSingleStoredWayToFile(filename, item);
                    else
                        db.StoredWays.Add(item);

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


        public static void ProcessFullFileV4(string filename)
        {
            var fs = System.IO.File.OpenRead(filename);
            Log.WriteLog("Starting " + filename + " V4 data read at " + DateTime.Now);
            var source = new PBFOsmStreamSource(fs);
            ProcessFileCoreV4(source);
        }

        public static void ProcessFilePiecesV4(string filename, bool saveToFiles = false)
        {
            //load data. Testing with delaware for speed again.
            string pbfFilename = filename;
            var fs = System.IO.File.OpenRead(pbfFilename);

            Log.WriteLog("Starting " + pbfFilename + " V4 data read at " + DateTime.Now);
            var source = new PBFOsmStreamSource(fs);
            float north = source.Where(s => s.Type == OsmGeoType.Node).Max(s => (float)((OsmSharp.Node)s).Latitude);
            float south = source.Where(s => s.Type == OsmGeoType.Node).Min(s => (float)((OsmSharp.Node)s).Latitude);
            float west = source.Where(s => s.Type == OsmGeoType.Node).Min(s => (float)((OsmSharp.Node)s).Longitude);
            float east = source.Where(s => s.Type == OsmGeoType.Node).Max(s => (float)((OsmSharp.Node)s).Longitude);
            var minsouth = (float)Math.Truncate(south);
            var minWest = (float)Math.Truncate(west);
            var maxNorth = (float)Math.Truncate(north) + 1;
            var maxEast = (float)Math.Truncate(east) + 1;
            Log.WriteLog("Bounding box for provided file determined at " + DateTime.Now + ", splitting into " + ((maxNorth - minsouth) * (maxEast - minWest)) + " sub-passes.");
            source.Dispose(); source = null;
            //fs.Close(); fs.Dispose(); fs = null;

            HashSet<long> relationsToSkip = new HashSet<long>();
            bool testMultithreaded = false;

            //TODO: both sides of this if check should respect the SaveToFiles parameter and save equivalent file names.
            if (!testMultithreaded)
            {
                for (var i = minWest; i < maxEast; i++)
                    for (var j = minsouth; j < maxNorth; j++)
                    {
                        var degreeBox = new PBFOsmStreamSource(fs).FilterBox(west, north, east, south, true);
                        var loadedRelations = ProcessFileCoreV4(degreeBox, false);
                        //var loadedRelations = V4Import.ProcessDegreeAreaV4(j, i, pbfFilename, saveToFiles); //This happens to be a 4-digit PlusCode, being 1 degree square.
                        foreach (var lr in loadedRelations)
                            relationsToSkip.Add(lr);
                    }
            }
            else
            {
                //This is the multi-thread variant. It seems to work, though I don't know what its ceiling is on performance.
                List<string> tempFiles = new List<string>();
                List<Task<List<long>>> taskStatuses = new List<Task<List<long>>>();
                for (var i = minWest; i < maxEast; i++)
                    for (var j = minsouth; j < maxNorth; j++)
                    {
                        fs = File.OpenRead(pbfFilename);
                        source = new PBFOsmStreamSource(fs);
                        var filtered = source.FilterBox(i, j + 1, i + 1, j, true);
                        string tempFile = ParserSettings.PbfFolder + "tempFile-" + i + "-" + j + ".pbf";
                        using (var destFile = new FileInfo(tempFile).Open(FileMode.Create))
                        {
                            tempFiles.Add(tempFile);
                            var target = new PBFOsmStreamTarget(destFile);
                            target.RegisterSource(filtered);
                            target.Pull();
                        }
                        Task<List<long>> process = Task.Run(() => ProcessFileCoreV4(filtered, true, tempFile)); // V4Import.ProcessDegreeAreaV4(j, i, tempFile, saveToFiles));
                        taskStatuses.Add(process);
                    }
                Task.WaitAll(taskStatuses.ToArray());
                foreach (var t in taskStatuses)
                {
                    foreach (var id in t.Result)
                        relationsToSkip.Add(id);
                }
                foreach (var tf in tempFiles)
                    File.Delete(tf);
                //end multithread variant.
            }

            //special pass for missed elements. Some things, like Delaware Bay, don't show up on this process
            //(Why isn't clear but its some OsmSharp behavior. Relations that aren't entirely contained in the filter area are excluded, I think?)
            Log.WriteLog("Attempting to reload missed elements...");
            fs = System.IO.File.OpenRead(pbfFilename);
            source = new PBFOsmStreamSource(fs);
            PraxisContext db = new PraxisContext();
            var secondChance = source.ToComplete().Where(s => s.Type == OsmGeoType.Relation && !relationsToSkip.Contains(s.Id)); //logic change - only load relations we haven't tried yet.
            string extraFilename = ParserSettings.JsonMapDataFolder + Path.GetFileNameWithoutExtension(pbfFilename) + "-additional.json";
            foreach (var sc in secondChance)
            {
                var found = GeometrySupport.ConvertOsmEntryToStoredWay(sc);
                if (found != null)
                {
                    if (saveToFiles)
                        GeometrySupport.WriteSingleStoredWayToFile(extraFilename, found);
                    else
                        db.StoredWays.Add(found);
                }
            }
            if (!saveToFiles)
            {
                Log.WriteLog("Saving final data....");
                db.SaveChanges();                
            }
            Log.WriteLog("Final pass completed at " + DateTime.Now);
        }

        //public static List<long> ProcessDegreeAreaV4(float south, float west, string filename, bool saveToFile = false)
        //{
        //    List<long> processedRelations = new List<long>(); //return this, so the parent function knows what to look for in a full-pass.
        //    var destFilename = ParserSettings.JsonMapDataFolder + Path.GetFileNameWithoutExtension(filename) + ".json";

        //    if (saveToFile)
        //        WriteSingleStoredWayToFile(destFilename, null, open: true);

        //    Log.WriteLog("Starting " + filename + " V4 data read at " + DateTime.Now);
        //    var fs = new FileStream(filename, FileMode.Open);
        //    var source = new PBFOsmStreamSource(fs);
        //    HashSet<long> waysToSkip = new HashSet<long>();

        //    //One degree square shouldn't use up more than 8GB of RAM. I can cut this to half a degree, spend 4x longer reading files from disk,
        //    //and totally avoid any OOM errors on almost any machine.
        //    Log.WriteLog("Box from " + south + "," + west + " to " + (south + 1) + "," + (west + 1));
        //    var thisBox = source.FilterBox(west, south + 1, west + 1, south, true);
        //    var relations = thisBox.AsParallel()
        //    .ToComplete()
        //    .Where(p => p.Type == OsmGeoType.Relation);

        //    var db = new PraxisContext();
        //    db.ChangeTracker.AutoDetectChangesEnabled = false;

        //    long relationCounter = 0;
        //    double totalCounter = 0;
        //    double totalRelations = relations.Count();
        //    long totalItems = 0;
        //    DateTime startedProcess = DateTime.Now;
        //    TimeSpan difference = DateTime.Now - DateTime.Now;

        //    Log.WriteLog("Loading relation data into RAM...");
        //    foreach (var r in relations) //This is where the first memory peak hits. it does load everything into memory
        //    {
        //        if (totalCounter == 0)
        //        {
        //            Log.WriteLog("Data loaded.");
        //            startedProcess = DateTime.Now;
        //            difference = startedProcess - startedProcess;
        //        }
        //        totalCounter++;
        //        try
        //        {
        //            processedRelations.Add(r.Id);
        //            var convertedRelation = GeometrySupport.ConvertOsmEntryToStoredWay(r);
        //            if (convertedRelation == null)
        //            {
        //                continue;
        //            }

        //            if (saveToFile)
        //                WriteSingleStoredWayToFile(destFilename, convertedRelation);
        //            else
        //                db.StoredWays.Add(convertedRelation);

        //            totalItems++;
        //            relationCounter++;
        //            if (relationCounter > 100)
        //            {
        //                ReportProgress(startedProcess, totalRelations, totalCounter, "Relations");
        //                relationCounter = 0;
        //            }

        //            foreach (var w in ((OsmSharp.Complete.CompleteRelation)r).Members)
        //            {
        //                if (w.Role == "outer") //Inner ways might have a tag match to apply later.
        //                    waysToSkip.Add(w.Member.Id);
        //            }
        //        }
        //        catch (Exception ex)
        //        {
        //            Log.WriteLog("Error Processing Relation " + r.Id + ": " + ex.Message);
        //        }
        //    } //);


        //    Log.WriteLog("Relations loaded at " + DateTime.Now);

        //    var ways = thisBox.AsParallel()
        //    .ToComplete()
        //    .Where(p => p.Type == OsmGeoType.Way && !waysToSkip.Contains(p.Id)); //avoid loading skippable Ways into RAM in the first place.

        //    double wayCounter = 0;
        //    double totalWays = ways.Count();
        //    totalCounter = 0;
        //    var fourPercentWays = totalWays / 25;

        //    Log.WriteLog("Loading Way data into RAM...");
        //    foreach (var w in ways) //Testing looks like multithreading doesn't provide any improvement here.
        //    {
        //        if (totalCounter == 0)
        //        {
        //            Log.WriteLog("Data Loaded");
        //            startedProcess = DateTime.Now;
        //            difference = DateTime.Now - DateTime.Now;
        //        }
        //        totalCounter++;
        //        try
        //        {
        //            totalItems++;
        //            wayCounter++;
        //            var item = GeometrySupport.ConvertOsmEntryToStoredWay(w);
        //            if (item == null)
        //                continue;

        //            if (saveToFile)
        //                WriteSingleStoredWayToFile(destFilename, item);
        //            else
        //                db.StoredWays.Add(item);

        //            if (wayCounter > 10000)
        //            {
        //                ReportProgress(startedProcess, totalWays, totalCounter, "Ways");
        //                wayCounter = 0;
        //            }
        //        }
        //        catch (Exception ex)
        //        {
        //            if (w == null)
        //                Log.WriteLog("Error Processing Way : Way was null");
        //            else
        //                Log.WriteLog("Error Processing Way " + w.Id + ": " + ex.Message);
        //        }
        //    } //);

        //    Log.WriteLog("Ways loaded at " + DateTime.Now);

        //    var defaultColor = SKColor.Parse(defaultTagParserEntries.Last().HtmlColorCode);
        //    var points = thisBox.AsParallel()
        //    .ToComplete() //unnecessary for nodes, but needed for the converter function.
        //    .Where(p => p.Type == OsmGeoType.Node && p.Tags.Count > 0 && GetStyleForOsmWay(p.Tags.Select(t => new WayTags() { Key = t.Key, Value = t.Value }).ToList()).paint.Color != defaultColor);
        //    double nodeCounter = 0;
        //    double totalnodes = points.Count();
        //    totalCounter = 0;
        //    Log.WriteLog("Loading Node data into RAM...");
        //    foreach (OsmSharp.Node p in points)
        //    {
        //        if (totalCounter == 0)
        //        {
        //            //Log.WriteLog("Node Data Loaded"); //For nodes, it does actually stream them as it reads the file.
        //            startedProcess = DateTime.Now;
        //            difference = DateTime.Now - DateTime.Now;
        //        }
        //        totalCounter++;
        //        try
        //        {
        //            totalItems++;
        //            nodeCounter++;
        //            var item = GeometrySupport.ConvertOsmEntryToStoredWay(p);
        //            if (item == null)
        //                continue;

        //            if (saveToFile)
        //                WriteSingleStoredWayToFile(destFilename, item);
        //            else
        //                db.StoredWays.Add(item);

        //            if (nodeCounter > 1000)
        //            {
        //                ReportProgress(startedProcess, totalnodes, totalCounter, "Nodes");
        //                nodeCounter = 0;
        //            }
        //        }
        //        catch (Exception ex)
        //        {
        //            if (p == null)
        //                Log.WriteLog("Error Processing Node: Node was null");
        //            else
        //                Log.WriteLog("Error Processing Node  " + p.Id + ": " + ex.Message);
        //        }
        //    }

        //    if (!saveToFile)
        //    {
        //        Log.WriteLog("Saving " + totalItems + " entries to the database.....");
        //        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        //        sw.Start();
        //        db.SaveChanges();
        //        sw.Stop();
        //        Log.WriteLog("Saving complete in " + sw.Elapsed + ".");
        //    }

        //    source.Dispose(); source = null;
        //    fs.Close(); fs.Dispose(); fs = null;

        //    return processedRelations;
        //}

        public static void ProcessV4MinimumRam(string filename) //TODO complete this. Probably replaces ProcessCore.
        {
            //Minimum RAM mode is going to be a fair amount slower, but should work on a wider array of home computers.
            //Differences from normal mode:
            //Writes data to json files, one element at a time.
            //processes in .5 degree chunks instead 1 degree chunks. (4x as many passes, 1/4th the RAM roughly).
            //Always write directly to json file, not DB.
        }


        public static void SplitPbfToSubfiles(string filename)
        {
            Log.WriteLog("Splitting " + filename + " into square degree files. " + DateTime.Now);
            var fs = new FileStream(filename, FileMode.Open);
            var source = new PBFOsmStreamSource(fs);

            double north = -360, south = 360, east = -360, west = 360;
            DateTime processStart = DateTime.Now;
            int counter = 0;
            var list = source.Where(s => s.Type == OsmGeoType.Node);
            //This loop takes ~15 minutes on my dev PC for the northamerica-latest file to read through the nodes, then 5 extra minutes to skim the rest of the file.
            //You still want to use the smallest file you can for this.
            foreach (OsmSharp.Node node in list)
            {
                if (node.Latitude.Value > north)
                    north = node.Latitude.Value;
                else if (node.Latitude.Value < south)
                    south = node.Latitude.Value;

                if (node.Longitude.Value < west)
                    west = node.Longitude.Value;
                else if (node.Longitude.Value > east)
                    east = node.Longitude.Value;

                counter++;
                if (counter % 10000000 == 0) //Planet.osm has 8 billion nodes, for reference.
                {
                    ReportProgress(processStart, 0, counter, "Nodes for bounding box");
                    Log.WriteLog("Current bounds are " + south + "," + west + " to " + north + "," + east);
                }
            }
            var minsouth = (float)Math.Truncate(south);
            var minWest = (float)Math.Truncate(west);
            var maxNorth = (float)Math.Truncate(north) + 1;
            var maxEast = (float)Math.Truncate(east) + 1;

            //North America values.
            //minsouth = 6;
            //minWest = -180;
            //maxNorth = 84;
            //maxEast = -4;
            Log.WriteLog("Bounding box for provided file determined at " + DateTime.Now + ", splitting into " + ((maxNorth - minsouth) * (maxEast - minWest)) + " sub-files.");

            //it look likes this takes over 30 minutes on my dev PC to read through the parent file. It's going to split the continent into 13,728 sub-files
            //Thats something like 9+ months of processing one file. Not acceptable. Need a new plan.
            for (var i = minWest; i < maxEast; i++)
                for (var j = minsouth; j < maxNorth; j++)
                {
                    string tempFile = ParserSettings.PbfFolder + "splitFile-" + i + "-" + j + ".pbf";
                    if (File.Exists(tempFile))
                    {
                        Log.WriteLog("File " + tempFile + " already exists, skipping.");
                        continue;
                    }
                    using (var destFile = new FileInfo(tempFile).Open(FileMode.Create))
                    {
                        var filtered = source.FilterBox(i, j + 1, i + 1, j, true);
                        var target = new PBFOsmStreamTarget(destFile);
                        target.RegisterSource(filtered);
                        target.Pull();
                    }
                }
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
