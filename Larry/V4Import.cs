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
        public static void ProcessFileV4(string filename, bool saveToFiles = false)
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
            fs.Close(); fs.Dispose(); fs = null;

            HashSet<long> relationsToSkip = new HashSet<long>();
            bool testMultithreaded = false;

            if (!testMultithreaded)
            {
                for (var i = minWest; i < maxEast; i++)
                    for (var j = minsouth; j < maxNorth; j++)
                    {
                        var loadedRelations = V4Import.ProcessDegreeAreaV4(j, i, pbfFilename, saveToFiles); //This happens to be a 4-digit PlusCode, being 1 degree square.
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
                        Task<List<long>> process = Task.Run(() => V4Import.ProcessDegreeAreaV4(j, i, tempFile, saveToFiles));
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
            string extraFilename = "";
            if (saveToFiles)
            {
                extraFilename = ParserSettings.JsonMapDataFolder + Path.GetFileNameWithoutExtension(pbfFilename) + "-additional.json";
                WriteSingleStoredWayToFile(extraFilename, null, true, false);
            }
            foreach (var sc in secondChance)
            {
                var found = GeometrySupport.ConvertOsmEntryToStoredWay(sc);
                if (found != null)
                {
                    if (saveToFiles)
                        WriteSingleStoredWayToFile(extraFilename, found);
                    else
                        db.StoredWays.Add(found);
                }
            }
            if (saveToFiles)
            {
                WriteSingleStoredWayToFile(extraFilename, null, false, true);
                Log.WriteLog("Final pass completed at " + DateTime.Now);
            }
            else
            {
                Log.WriteLog("Saving final data....");
                db.SaveChanges();
                Log.WriteLog("Final pass completed at " + DateTime.Now);
            }
        }

        public static List<long> ProcessDegreeAreaV4(float south, float west, string filename, bool saveToFile = false)
        {
            List<long> processedRelations = new List<long>(); //return this, so the parent function knows what to look for in a full-pass.

            if (saveToFile)
                WriteSingleStoredWayToFile(filename + ".json", null, open: true);

            Log.WriteLog("Starting " + filename + " V4 data read at " + DateTime.Now);
            var fs = new FileStream(filename, FileMode.Open);
            var source = new PBFOsmStreamSource(fs);
            HashSet<long> waysToSkip = new HashSet<long>();

            //One degree square shouldn't use up more than 8GB of RAM. I can cut this to half a degree, spend 4x longer reading files from disk,
            //and totally avoid any OOM errors on almost any machine.
            Log.WriteLog("Box from " + south + "," + west + " to " + (south + 1) + "," + (west + 1));
            var thisBox = source.FilterBox(west, south + 1, west + 1, south, true);
            var relations = thisBox.AsParallel()
            .ToComplete()
            .Where(p => p.Type == OsmGeoType.Relation);

            var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            long relationCounter = 0;
            double totalCounter = 0;
            double totalRelations = relations.Count();
            long totalItems = 0;
            DateTime startedProcess = DateTime.Now;
            TimeSpan difference = DateTime.Now - DateTime.Now;

            Log.WriteLog("Loading relation data into RAM...");
            foreach (var r in relations) //This is where the first memory peak hits. it does load everything into memory
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
                        WriteSingleStoredWayToFile(filename + ".json", convertedRelation);
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
            } //);


            Log.WriteLog("Relations loaded at " + DateTime.Now);

            var ways = thisBox.AsParallel()
            .ToComplete()
            .Where(p => p.Type == OsmGeoType.Way && !waysToSkip.Contains(p.Id)); //avoid loading skippable Ways into RAM in the first place.

            double wayCounter = 0;
            double totalWays = ways.Count();
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
                        WriteSingleStoredWayToFile(filename + ".json", item);
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
            } //);

            Log.WriteLog("Ways loaded at " + DateTime.Now);

            var defaultColor = SKColor.Parse(defaultTagParserEntries.Last().HtmlColorCode);
            var points = thisBox.AsParallel()
            .ToComplete() //unnecessary for nodes, but needed for the converter function.
            .Where(p => p.Type == OsmGeoType.Node && p.Tags.Count > 0 && GetStyleForOsmWay(p.Tags.Select(t => new WayTags() { Key = t.Key, Value = t.Value }).ToList()).Color != defaultColor);
            double nodeCounter = 0;
            double totalnodes = points.Count();
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
                        WriteSingleStoredWayToFile(filename + ".json", item);
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

            source.Dispose(); source = null;
            fs.Close(); fs.Dispose(); fs = null;

            return processedRelations;
        }

        public static void ProcessV4MinimumRam(string filename)
        {
            //Minimum RAM mode is going to be a fair amount slower, but should work on a wider array of home computers.
            //Differences from normal mode:
            //Writes data to json files, one element at a time.
            //processes in .5 degree chunks instead 1 degree chunks. (4x as many passes, 1/4th the RAM roughly).
            //Always write directly to json file, not DB.
        }

        public static void WriteStoredWayListToFile(string filename, ref List<StoredWay> mapdata)
        {
            System.IO.StreamWriter sw = new StreamWriter(filename);
            sw.Write("[" + Environment.NewLine);
            foreach (var md in mapdata)
            {
                if (md != null) //null can be returned from the functions that convert OSM entries to MapData
                {
                    var recordVersion = new CoreComponents.Support.StoredWayForJson(md.id, md.name, md.sourceItemID, md.sourceItemType, md.wayGeometry.AsText(), string.Join("~", md.WayTags.Select(t => t.storedWay + "|" + t.Key + "|" + t.Value)), md.IsGameElement);
                    var test = JsonSerializer.Serialize(recordVersion, typeof(CoreComponents.Support.StoredWayForJson));
                    sw.Write(test);
                    sw.Write("," + Environment.NewLine);
                }
            }
            sw.Write("]");
            sw.Close();
            sw.Dispose();
            Log.WriteLog("All MapData entries were serialized individually and saved to file at " + DateTime.Now);
        }

        public static void WriteSingleStoredWayToFile(string filename, StoredWay md, bool open = false, bool close = false)
        {
            //System.IO.StreamWriter sw = new StreamWriter(filename, true);
            if (open)
                File.AppendAllText(filename, "[" + Environment.NewLine);

            if (md != null) //null can be returned from the functions that convert OSM entries to MapData
            {
                var recordVersion = new CoreComponents.Support.StoredWayForJson(md.id, md.name, md.sourceItemID, md.sourceItemType, md.wayGeometry.AsText(), string.Join("~", md.WayTags.Select(t => t.storedWay + "|" + t.Key + "|" + t.Value)), md.IsGameElement);
                var test = JsonSerializer.Serialize(recordVersion, typeof(CoreComponents.Support.StoredWayForJson));
                File.AppendAllText(filename, test + "," + Environment.NewLine);
            }

            if (close)
                File.AppendAllText(filename, "]");
        }

        public static List<StoredWay> ReadStoredWaysFileToMemory(string filename)
        {
            StreamReader sr = new StreamReader(filename);
            List<StoredWay> lm = new List<StoredWay>();
            lm.Capacity = 100000;
            JsonSerializerOptions jso = new JsonSerializerOptions();
            jso.AllowTrailingCommas = true;

            NetTopologySuite.IO.WKTReader reader = new NetTopologySuite.IO.WKTReader();
            reader.DefaultSRID = 4326;

            while (!sr.EndOfStream)
            {
                string line = sr.ReadLine();
                if (line == "[")
                {
                    //start of a file that spaced out every entry on a newline correctly. Skip.
                }
                else if (line == "]")
                {
                    //dont do anything, this is EOF
                }
                else //The standard line
                {
                    StoredWayForJson j = (StoredWayForJson)JsonSerializer.Deserialize(line.Substring(0, line.Count() - 1), typeof(StoredWayForJson), jso);
                    var temp = new StoredWay() { id = j.id, name = j.name, sourceItemID = j.sourceItemID, sourceItemType = j.sourceItemType, wayGeometry = reader.Read(j.wayGeometry), IsGameElement = j.IsGameElement };
                    if (!string.IsNullOrWhiteSpace(j.WayTags))
                    {
                        var tagData = j.WayTags.Split("~");
                        if (tagData.Count() > 0)
                        {
                            foreach (var tag in tagData)
                            {
                                var elements = tag.Split("|");
                                WayTags wt = new WayTags();
                                wt.storedWay = temp;
                                wt.Key = elements[1];
                                wt.Value = elements[2];
                            }
                        }
                    }

                    if (temp.wayGeometry is Polygon)
                    {
                        temp.wayGeometry = GeometrySupport.CCWCheck((Polygon)temp.wayGeometry);
                    }
                    if (temp.wayGeometry is MultiPolygon)
                    {
                        MultiPolygon mp = (MultiPolygon)temp.wayGeometry;
                        for (int i = 0; i < mp.Geometries.Count(); i++)
                        {
                            mp.Geometries[i] = GeometrySupport.CCWCheck((Polygon)mp.Geometries[i]);
                        }
                        temp.wayGeometry = mp;
                    }
                    lm.Add(temp);
                }
            }

            if (lm.Count() == 0)
                Log.WriteLog("No entries for " + filename + "? why?");

            sr.Close(); sr.Dispose();
            Log.WriteLog("EOF Reached for " + filename + " at " + DateTime.Now);
            return lm;
        }

        public static StoredWay ConvertSingleJsonStoredWay(string sw)
        {
            JsonSerializerOptions jso = new JsonSerializerOptions();
            jso.AllowTrailingCommas = true;
            NetTopologySuite.IO.WKTReader reader = new NetTopologySuite.IO.WKTReader();
            reader.DefaultSRID = 4326;

            StoredWayForJson j = (StoredWayForJson)JsonSerializer.Deserialize(sw.Substring(0, sw.Count() - 1), typeof(StoredWayForJson), jso);
            var temp = new StoredWay() { id = j.id, name = j.name, sourceItemID = j.sourceItemID, sourceItemType = j.sourceItemType, wayGeometry = reader.Read(j.wayGeometry), IsGameElement = j.IsGameElement };
            if (!string.IsNullOrWhiteSpace(j.WayTags))
            {
                var tagData = j.WayTags.Split("~");
                if (tagData.Count() > 0)
                {
                    foreach (var tag in tagData)
                    {
                        var elements = tag.Split("|");
                        WayTags wt = new WayTags();
                        wt.storedWay = temp;
                        wt.Key = elements[1];
                        wt.Value = elements[2];
                    }
                }
            }

            if (temp.wayGeometry is Polygon)
            {
                temp.wayGeometry = GeometrySupport.CCWCheck((Polygon)temp.wayGeometry);
            }
            if (temp.wayGeometry is MultiPolygon)
            {
                MultiPolygon mp = (MultiPolygon)temp.wayGeometry;
                for (int i = 0; i < mp.Geometries.Count(); i++)
                {
                    mp.Geometries[i] = GeometrySupport.CCWCheck((Polygon)mp.Geometries[i]);
                }
                temp.wayGeometry = mp;
            }
            return temp;
        }

        public static void ReportProgress(DateTime startedProcess, double totalItems, double itemsProcessed, string itemName)
        {
            var difference = DateTime.Now - startedProcess;
            double percentage = (itemsProcessed / totalItems) * 100;
            var entriesPerSecond = itemsProcessed / difference.TotalSeconds;
            var secondsLeft = (totalItems - itemsProcessed) / entriesPerSecond;
            TimeSpan estimatedTime = TimeSpan.FromSeconds(secondsLeft);
            Log.WriteLog(Math.Round(entriesPerSecond) + " " + itemName + " per second processed, " + Math.Round(percentage, 2) + "% done, estimated time remaining: " + estimatedTime.ToString());
        }
    }
}
