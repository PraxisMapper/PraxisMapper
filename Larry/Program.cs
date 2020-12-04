using CoreComponents;
using CoreComponents.Support;
using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using OsmSharp;
using OsmSharp.Complete;
using OsmSharp.Streams;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using static CoreComponents.DbTables;
using static CoreComponents.MapSupport;

//TODO: since some of these .pbf files become larger as trimmed JSON instead of smaller, maybe I should try a path that writes directly to DB from PBF? might involve 
//TODO: Add high-verbosity logging messages.
//TODO: set option flag to enable writing MapData entries to DB or File. Especially since bulk inserts won't fly for MapData from files, apparently.
//TODO: look into using Span<T> instead of lists? This might be worth looking at performance differences. (and/or Memory<T>, which might be a parent for Spans)
//TODO: Ponder using https://download.bbbike.org/osm/ as a data source to get a custom extract of an area (for when users want a local-focused app, probably via a wizard GUI)

namespace Larry
{
    class Program
    {
        //NOTE: OSM data license allows me to use the data but requires acknowleging OSM as the data source

        static void Main(string[] args)
        {
            var memMon = new MemoryMonitor();
            PraxisContext.connectionString = ParserSettings.DbConnectionString;

            if (args.Count() == 0)
            {
                Console.WriteLine("You must pass an arguement to this application");
                //TODO: list args
                return;
            }

            //Check for logging arguement first before running any commands.
            if (args.Any(a => a == "-v" || a == "-verbose"))
                Log.Verbosity = Log.VerbosityLevels.High;

            if (args.Any(a => a == "-noLogs"))
                Log.Verbosity = Log.VerbosityLevels.Off;

            if (args.Any(a => a == "-processEverything"))
            {
                DbSettings.processRoads = true;
                DbSettings.processBuildings = true;
                DbSettings.processParking = true;
            }

            if (args.Any(a => a == "-spaceSaver"))
            {
                ParserSettings.UseHighAccuracy = false;
                MapSupport.factory = NtsGeometryServices.Instance.CreateGeometryFactory(new PrecisionModel(1000000), 4326); //SRID matches Plus code values.  Precision model means round all points to 7 decimal places to not exceed float's useful range.
                MapSupport.SimplifyAreas = true;
            }

            //If multiple args are supplied, run them in the order that make sense, not the order the args are supplied.

            if (args.Any(a => a == "-createDB")) //setup the destination database
            {
                PraxisContext db = new PraxisContext();
                db.Database.EnsureCreated(); //all the automatic stuff EF does for us, without migrations.
                //Not automatic entries executed below:
                db.Database.ExecuteSqlRaw(PraxisContext.MapDataValidTrigger);
                db.Database.ExecuteSqlRaw(PraxisContext.GeneratedMapDataValidTrigger);
                db.Database.ExecuteSqlRaw(PraxisContext.MapDataIndex);
                db.Database.ExecuteSqlRaw(PraxisContext.GeneratedMapDataIndex);
                db.Database.ExecuteSqlRaw(PraxisContext.FindDBMapDataBounds);
                MapSupport.InsertAreaTypesToDb();
            }

            if (args.Any(a => a == "-cleanDB"))
            {
                CleanDb();
            }

            if (args.Any(a => a == "-singleTest"))
            {
                //Check on a specific thing. Not an end-user command.
                //Current task: Identify issue with relation
                SingleTest();
            }

            if (args.Any(a => a == "-resetXml" || a == "-resetPbf")) //update both anyways.
            {
                List<string> filenames = System.IO.Directory.EnumerateFiles(ParserSettings.PbfFolder, "*.*Done").ToList();
                foreach (var file in filenames)
                {
                    File.Move(file, file.Substring(0, file.Length - 4));
                }
            }

            if (args.Any(a => a == "-resetJson"))
            {
                List<string> filenames = System.IO.Directory.EnumerateFiles(ParserSettings.JsonMapDataFolder, "*.jsonDone").ToList();
                foreach (var file in filenames)
                {
                    File.Move(file, file.Substring(0, file.Length - 4));
                }
            }

            if (args.Any(a => a == "-trimPbfFiles"))
            {
                MakeAllSerializedFilesFromPBF();
            }

            if (args.Any(a => a.StartsWith("-trimPbfsByType")))
            {
                List<string> filenames = System.IO.Directory.EnumerateFiles(ParserSettings.PbfFolder, "*.pbf").ToList();
                foreach (string filename in filenames)
                    SerializeSeparateFilesFromPBF(filename);
            }

            if (args.Any(a => a == "-readMapData"))
            {
                AddMapDataToDBFromFiles();
            }

            if (args.Any(a => a == "-removeDupes"))
            {
                RemoveDuplicates();
            }

            if (args.Any(a => a.StartsWith("-createStandalone")))
            {
                //Near-future plan: make an app that covers an area and pulls in all data for it.
                //like a university or a park. Draws ALL objects there to an SQLite DB used directly by an app, no data connection.
                //Second arg: area covered somehow (way or relation ID of thing to use? big plus code?)
                //Get a min and a max point, make a box, load all the elements in that from the parent file.

                //TODO: switch this to make a detailed entry for a relation.
                //So i need a second parameter for relationID, then use that to pull the envelope for its bound
                //and output a file with all that data.

                //Test sample covers OSU. OSU is .0307 x .0154 degrees.
                GeoPoint min = new GeoPoint(39.9901, -83.0496);
                GeoPoint max = new GeoPoint(40.0208, -83.0035);
                GeoArea box = new GeoArea(min, max);
                string filename = ParserSettings.PbfFolder + "ohio-latest.osm.pbf";

                int relationId = args.Where(a => a.StartsWith("-createStandalone")).First().Split('|')[1].ToInt();
                CreateStandaloneDB(relationId, filename);
            }

            if (args.Any(a => a.StartsWith("-checkFile:")))
            {
                //scan a file for information on what will or won't load.
                string arg = args.Where(a => a.StartsWith("-checkFile:")).First().Replace("-checkFile:", "");
                ValidateFile(arg);
            }

            if (args.Any(a => a.StartsWith("-gen6CellMapTiles:")))
            {
                //TODO still in process
                //TODO move to function
                //make tiles with pixels equal to the selected plus code resolution.
                string resolution = args.Where(a => a.StartsWith("-gen6CellMapTiles:")).First().Replace("-gen6CellMapTiles:", "");
                //I have to figure out how to find the min/max area covered by the server.
                var db = new PraxisContext();

                //still have to code this logic in somehow.
                //Log.WriteLog("Finding minimum point from DB");
                //var minPoint = db.MapData.Min(md => md.place.Boundary.Coordinates.Min());
                //Log.WriteLog("Finding maximum point from DB");
                //var maxPoint = db.MapData.Max(md => md.place.Boundary.Coordinates.Max());

                //hardcoding values for now.
                //minimumPointLon	minimumPointLat	maximumPointLon	maximumPointLat
                //-85.111908	38.1447449	-80.3286743    42.070076
                var minPlusCode = new OpenLocationCode(new GeoPoint(38.1447449, -85.111908));
                var maxPlusCode = new OpenLocationCode(new GeoPoint(42.070076, -80.3286743));
                //shift these to 8s or 6s to get a list we could enumerate over?

                var minPlusCodePoint = OpenLocationCode.DecodeValid(minPlusCode.CodeDigits.Substring(0, 6));
                var maxPlusCodePoint = OpenLocationCode.DecodeValid(maxPlusCode.CodeDigits.Substring(0, 6));
                if (resolution == "10")
                {
                    for (double x = minPlusCodePoint.Min.Longitude; x <= maxPlusCodePoint.Max.Longitude; x += MapSupport.resolutionCell6)
                        for (double y = minPlusCodePoint.Min.Latitude; y <= maxPlusCodePoint.Max.Latitude; y += MapSupport.resolutionCell6)
                        {
                            GeoArea tileArea = new GeoArea(new GeoPoint(y, x), new GeoPoint(y + MapSupport.resolutionCell6, x + MapSupport.resolutionCell6));
                            var places = MapSupport.GetPlaces(tileArea);
                            var tileData = MapSupport.DrawAreaMapTile(ref places, tileArea);
                            db.MapTiles.Add(new MapTile() { CreatedOn = DateTime.Now, mode = 1,  tileData = tileData,  resolutionScale = 10, PlusCode = OpenLocationCode.Encode(new GeoPoint(y, x)).Substring(0, 6) });
                            db.SaveChanges();
                        }
                }

                if (resolution == "11")
                {
                    for (double x = minPlusCodePoint.Min.Longitude; x <= maxPlusCodePoint.Max.Longitude; x += MapSupport.resolutionCell6)
                        for (double y = minPlusCodePoint.Min.Latitude; y <= maxPlusCodePoint.Max.Latitude; y += MapSupport.resolutionCell6)
                        {
                            GeoArea tileArea = new GeoArea(new GeoPoint(y, x), new GeoPoint(y + MapSupport.resolutionCell6, x + MapSupport.resolutionCell6));
                            var places = MapSupport.GetPlaces(tileArea);
                            var tileData = MapSupport.DrawAreaMapTile11(ref places, tileArea);
                            db.MapTiles.Add(new MapTile() { CreatedOn = DateTime.Now, mode = 1, tileData = tileData, resolutionScale = 11, PlusCode = OpenLocationCode.Encode(new GeoPoint(y, x)).Substring(0, 6) });
                            db.SaveChanges();
                        }
                }
            }

            if (args.Any(a => a == "-extractBigAreas"))
            {
                //ExtractAreasFromLargeFile(ParserSettings.PbfFolder + "planet-latest.osm.pbf"); //Guarenteed to have everything. Estimates 103 minutes per relation.
                ExtractAreasFromLargeFile(ParserSettings.PbfFolder + "north-america-latest.osm.pbf");
            }

            if (args.Any(a => a.StartsWith("-populateEmptyArea:")))
            {
                //NOTE: 86GXVH appears to be entirely empty of interesting features, it's a good spot to start testing if you want to generate lots of areas.
                var db = new PraxisContext();
                var cell6 = args.Where(a => a.StartsWith("-populateEmptyArea:")).First().Split(":")[1];
                CodeArea box6 = OpenLocationCode.DecodeValid(cell6);
                var coordSeq6 = GeoAreaToCoordArray(box6);
                var location6 = factory.CreatePolygon(coordSeq6);
                var places = db.MapData.Where(md => md.place.Intersects(location6) && md.AreaTypeId < 13).ToList();
                var fakeplaces = db.GeneratedMapData.Where(md => md.place.Intersects(location6)).ToList();

                for (int x = 0; x < 20; x++)
                {
                    for (int y = 0; y < 20; y++)
                    {
                        string cell8 = cell6 + OpenLocationCode.CodeAlphabet[x] + OpenLocationCode.CodeAlphabet[y];
                        CodeArea box = OpenLocationCode.DecodeValid(cell8);
                        var coordSeq = GeoAreaToCoordArray(box);
                        var location = factory.CreatePolygon(coordSeq);
                        if (!places.Any(md => md.place.Intersects(location)) && !fakeplaces.Any(md => md.place.Intersects(location)))
                            MapSupport.CreateInterestingAreas(cell8);
                    }
                }                
            }
            return;
        }

        public static void AddMapDataToDBFromFiles()
        {
            //This function is pretty slow. I should figure out how to speed it up. Approx. 3,000 ways per second right now.
            //Bulk inserts fail on the Geography type, last I had checked.
            foreach (var file in System.IO.Directory.EnumerateFiles(ParserSettings.JsonMapDataFolder, "*-MapData*.json"))
            {
                Console.Title = file;
                Log.WriteLog("Starting MapData read from  " + file + " at " + DateTime.Now);
                PraxisContext db = new PraxisContext();
                db.ChangeTracker.AutoDetectChangesEnabled = false; //Allows single inserts to operate at a reasonable speed (~6000 per second). Nothing else edits this table.
                List<MapData> entries = ReadMapDataToMemory(file);
                Log.WriteLog("Processing " + entries.Count() + " ways from " + file, Log.VerbosityLevels.High);
                //Trying to make this a little bit faster by working around internal EF graph stuff.
                for (int i = 0; i <= entries.Count() / 10000; i++)
                {
                    var subList = entries.Skip(i * 10000).Take(10000).ToList();
                    db.MapData.AddRange(subList);
                    db.SaveChanges();//~3seconds on dev machine per pass at 10k entries at once.
                    Log.WriteLog("Entry pass " + i + " of " + (entries.Count() / 10000) + " completed");
                }

                //db.MapData.AddRange(entries);
                //Log.WriteLog("Entries added to entities at " + DateTime.Now, Log.VerbosityLevels.High);
                //db.SaveChanges();

                Log.WriteLog("Added " + file + " to dB at " + DateTime.Now);
                File.Move(file, file + "Done");
            }
        }

        public static void CleanDb()
        {
            Log.WriteLog("Cleaning DB at " + DateTime.Now);
            PraxisContext osm = new PraxisContext();
            osm.Database.SetCommandTimeout(900);

            //Dont remove these automatically, these only get filled on DB creation
            //osm.Database.ExecuteSqlRaw("TRUNCATE TABLE AreaTypes");
            //Log.WriteLog("AreaTypes cleaned at " + DateTime.Now);

            osm.Database.ExecuteSqlRaw("TRUNCATE TABLE MapData");
            Log.WriteLog("MapData cleaned at " + DateTime.Now, Log.VerbosityLevels.High);
            osm.Database.ExecuteSqlRaw("TRUNCATE TABLE MapTiles");
            Log.WriteLog("MapTiles cleaned at " + DateTime.Now, Log.VerbosityLevels.High);
            osm.Database.ExecuteSqlRaw("TRUNCATE TABLE PerformanceInfo");
            Log.WriteLog("PerformanceInfo cleaned at " + DateTime.Now, Log.VerbosityLevels.High);
            osm.Database.ExecuteSqlRaw("TRUNCATE TABLE GeneratedMapData");
            Log.WriteLog("GeneratedMapData cleaned at " + DateTime.Now, Log.VerbosityLevels.High);

            Log.WriteLog("DB cleaned at " + DateTime.Now);
        }

        public static void MakeAllSerializedFilesFromPBF()
        {
            List<string> filenames = System.IO.Directory.EnumerateFiles(ParserSettings.PbfFolder, "*.pbf").ToList();
            foreach (string filename in filenames)
                SerializeFilesFromPBF(filename);
        }

        public static void SerializeFilesFromPBF(string filename)
        {
            System.IO.FileInfo fi = new FileInfo(filename);
            if (ParserSettings.ForceSeparateFiles || fi.Length > ParserSettings.FilesizeSplit) //I have 28 country/state level extracts over this size, and this should include the ones that cause the most issues.
            {
                //Parse this file into area type sub-files from disk, so that I dodge hit RAM limits
                SerializeSeparateFilesFromPBF(filename);
                return;
            }

            //else parse this file all at once.
            FileStream fs = new FileStream(filename, FileMode.Open);
            byte[] fileInRam = new byte[fs.Length];
            fs.Read(fileInRam, 0, (int)fs.Length);
            MemoryStream ms = new MemoryStream(fileInRam);
            fs.Close(); fs.Dispose();

            Log.WriteLog("Checking for members in  " + filename + " at " + DateTime.Now);
            string destFilename = System.IO.Path.GetFileName(filename).Replace(".osm.pbf", "");

            Log.WriteLog("Starting " + filename + " " + " data read at " + DateTime.Now);
            var osmRelations = GetRelationsFromStream(ms, null);
            Log.WriteLog(osmRelations.Count() + " relations found", Log.VerbosityLevels.High);
            var referencedWays = osmRelations.AsParallel().SelectMany(r => r.Members.Where(m => m.Type == OsmGeoType.Way).Select(m => m.Id)).Distinct().ToHashSet();

            Log.WriteLog(referencedWays.Count() + " ways used within relations", Log.VerbosityLevels.High);
            Log.WriteLog("Relations loaded at " + DateTime.Now);
            var osmWays = GetWaysFromStream(ms, null, referencedWays);
            Log.WriteLog(osmWays.Count() + " ways found", Log.VerbosityLevels.High);
            Log.WriteLog((osmWays.Count() - referencedWays.Count()) + " standalone ways pulled in.", Log.VerbosityLevels.High);
            var referencedNodes = osmWays.AsParallel().SelectMany(m => m.Nodes).Distinct().ToHashSet();
            Log.WriteLog("Ways loaded at " + DateTime.Now);
            var osmNodes = GetNodesFromStream(ms, null, referencedNodes);
            referencedNodes = null;
            Log.WriteLog("All relevant data pulled from file at " + DateTime.Now);
            ms.Close(); ms.Dispose();
            fileInRam = null;

            var processedEntries = ProcessData(osmNodes, ref osmWays, ref osmRelations, referencedWays);
            WriteMapDataToFile(ParserSettings.JsonMapDataFolder + destFilename + "-MapData" + (ParserSettings.UseHighAccuracy ? "-highAcc" : "-lowAcc") + ".json", ref processedEntries);
            processedEntries = null;

            Log.WriteLog("Processed " + filename + " at " + DateTime.Now);
            File.Move(filename, filename + "Done"); //We made it all the way to the end, this file is done.
        }

        public static void SerializeSeparateFilesFromPBF(string filename)
        {
            //This will read from disk, since we are assuming this file will hit RAM limits if we read it all at once.
            foreach (var areatype in areaTypes.Where(a => a.AreaTypeId < 100)) //each pass takes roughly the same amount of time to read, but uses less ram. 
            {
                //skip entries if the settings say not to process them. they'll get 0 tagged entries but don't waste time reading the file.
                //ParserSettings.???

                //if (areatype.AreaName == "water")
                //  continue; //Water is too big for my PC on files this side most of the time on the 5-6 worst files. Norway.pbf can hit 39GB committed memory.

                string areatypename = areatype.AreaName;
                Log.WriteLog("Checking for " + areatypename + " members in  " + filename + " at " + DateTime.Now);
                string destFilename = System.IO.Path.GetFileName(filename).Replace(".osm.pbf", "");

                Log.WriteLog("Starting " + filename + " " + areatypename + " data read at " + DateTime.Now);
                var osmRelations = GetRelationsFromPbf(filename, areatypename);
                Log.WriteLog(osmRelations.Count() + " relations found", Log.VerbosityLevels.High);
                var referencedWays = osmRelations.AsParallel().SelectMany(r => r.Members.Where(m => m.Type == OsmGeoType.Way).Select(m => m.Id)).Distinct().ToLookup(k => k, v => (short)0);
                var refWays2 = osmRelations.AsParallel().SelectMany(r => r.Members.Where(m => m.Type == OsmGeoType.Way).Select(m => m.Id)).Distinct().ToDictionary(k => k, v => (short)0);
                Log.WriteLog(referencedWays.Count() + " ways used within relations", Log.VerbosityLevels.High);
                Log.WriteLog("Relations loaded at " + DateTime.Now);
                var osmWays = GetWaysFromPbf(filename, areatypename, referencedWays);
                Log.WriteLog(osmWays.Count() + " ways found", Log.VerbosityLevels.High);
                Log.WriteLog((osmWays.Count() - referencedWays.Count()) + " standalone ways pulled in.", Log.VerbosityLevels.High);
                var referencedNodes = osmWays.AsParallel().SelectMany(m => m.Nodes).Distinct().ToLookup(k => k, v => (short)0);
                //var referencedNodes2 = osmWays.AsParallel().SelectMany(m => m.Nodes).Distinct().ToDictionary(k => k, v => (short)0);
                //var referencedNodes3 = osmWays.AsParallel().SelectMany(m => m.Nodes).Distinct().ToHashSet();
                Log.WriteLog(referencedNodes.Count() + " nodes used by ways", Log.VerbosityLevels.High);
                Log.WriteLog("Ways loaded at " + DateTime.Now);
                var osmNodes = GetNodesFromPbf(filename, areatypename, referencedNodes);
                referencedNodes = null;
                Log.WriteLog("Relevant data pulled from file at " + DateTime.Now);

                //Testing having this stream results to a file instead of making a list we write afterwards.
                var processedEntries = ProcessData(osmNodes, ref osmWays, ref osmRelations, referencedWays, true, ParserSettings.JsonMapDataFolder + destFilename + "-MapData-" + areatypename + (ParserSettings.UseHighAccuracy ? "-highAcc" : "-lowAcc") + ".json");
                //WriteMapDataToFile(ParserSettings.JsonMapDataFolder + destFilename + "-MapData-" + areatypename + (ParserSettings.UseHighAccuracy ? "-highAcc" : "-lowAcc") + ".json", ref processedEntries);
                processedEntries = null;
            }

            Log.WriteLog("Processed " + filename + " at " + DateTime.Now);
            File.Move(filename, filename + "Done"); //We made it all the way to the end, this file is done.
        }

        private static List<MapData> ProcessData(ILookup<long, NodeReference> osmNodes, ref List<OsmSharp.Way> osmWays, ref List<OsmSharp.Relation> osmRelations, ILookup<long, short> referencedWays, bool writeDirectly = false, string directFile = "")
        {
            //This way really needs an option to write data directly to the output file. I wonder how much time is spent resizing processedEntries.
            List<MapData> processedEntries = new List<MapData>();
            List<NodeData> nodes = new List<NodeData>();
            List<WayData> ways = new List<WayData>();

            nodes.Capacity = 100000;
            ways.Capacity = 100000;
            if (!writeDirectly)
                processedEntries.Capacity = 1000000; // osmWays.Count() + osmRelations.Count(); //8 million ways might mean this fails on a 32-bit int.

            System.IO.StreamWriter sw = new StreamWriter(directFile);
            if (writeDirectly)
            {
                sw.Write("[" + Environment.NewLine);
            }

            //Write nodes as mapdata if they're tagged separately from other things.
            Log.WriteLog("Finding tagged nodes at " + DateTime.Now);
            var taggedNodes = osmNodes.AsParallel().Where(n => n.First().name != "" && n.First().type != "" && n.First().type != null).ToList();
            if (!writeDirectly)
                processedEntries.AddRange(taggedNodes.AsParallel().Select(s => MapSupport.ConvertNodeToMapData(s.First())));
            else
            {
                foreach (var n in taggedNodes) //this can't be parallel because we need to write to a single file.
                {
                    var md = MapSupport.ConvertNodeToMapData(n.First());

                    if (md != null) //null can be returned from the functions that convert OSM entries to MapData
                    {
                        var recordVersion = new MapDataForJson(md.name, md.place.AsText(), md.type, md.WayId, md.NodeId, md.RelationId, md.AreaTypeId);
                        var test = JsonSerializer.Serialize(recordVersion, typeof(MapDataForJson));
                        sw.Write(test);
                        sw.Write("," + Environment.NewLine);
                    }
                }
            }
            Log.WriteLog("Standalone tagged nodes converted to MapData at " + DateTime.Now);
            taggedNodes = null;

            Log.WriteLog("Converting " + osmWays.Count() + " OsmWays to my Ways at " + DateTime.Now);
            ways.Capacity = osmWays.Count();
            ways = osmWays.AsParallel().Select(w => new WayData()
            {
                id = w.Id.Value,
                name = GetElementName(w.Tags),
                AreaType = MapSupport.GetElementType(w.Tags),
                nodRefs = w.Nodes.ToList()
            })
            .ToList();
            osmWays = null; //free up RAM we won't use again.
            Log.WriteLog("List created at " + DateTime.Now);

            int wayCounter = 0;
            System.Threading.Tasks.Parallel.ForEach(ways, (w) =>
            {
                wayCounter++;
                if (wayCounter % 10000 == 0)
                    Log.WriteLog(wayCounter + " processed so far");

                LoadNodesIntoWay(ref w, ref osmNodes);
            });

            Log.WriteLog("Ways populated with Nodes at " + DateTime.Now);
            osmNodes = null; //done with these now, can free up RAM again.

            //Process all the ways that aren't part of a relation first, then remove them.
            if (!writeDirectly)
            {
                processedEntries.AddRange(ways.Where(w => referencedWays[w.id].Count() == 0).AsParallel().Select(w => ConvertWayToMapData(ref w)));  //When we start hitting the swap file, this takes about 3-4 minutes to start a batch of entries on my dev machine.
                Log.WriteLog("Standalone tagged ways converted to MapData at " + DateTime.Now);
                ways = ways.Where(w => referencedWays[w.id].Count() > 0).ToList();
                processedEntries.AddRange(osmRelations.AsParallel().Select(r => ProcessRelation(r, ref ways))); //Approx. twice as fast as ProcessRelations() without parallel.
                Log.WriteLog("Relations converted to MapData at " + DateTime.Now);

                var outerWays = osmRelations.SelectMany(r => r.Members.Where(m => m.Role == "outer" && m.Type == OsmGeoType.Way).Select(m => m.Id)).ToLookup(k => k, v => v);
                ways = ways.Where(w => outerWays[w.id].Count() == 0).ToList();
                outerWays = null;
                osmRelations = null;

                processedEntries.AddRange(ways.AsParallel().Select(w => ConvertWayToMapData(ref w)));
                ways = null;
                return processedEntries;
            }
            else
            {
                foreach (var w1 in ways)
                {
                    if (referencedWays[w1.id].Count() != 0)
                            continue; //we're only loading ways that aren't tagged in another relation, so skip ones that are used elsewhere. Check this way to avoid converting ways into another IEnumerable

                    var w2 = w1;
                    var md2 = MapSupport.ConvertWayToMapData(ref w2);

                    if (md2 != null) //null can be returned from the functions that convert OSM entries to MapData
                    {
                        var recordVersion = new MapDataForJson(md2.name, md2.place.AsText(), md2.type, md2.WayId, md2.NodeId, md2.RelationId, md2.AreaTypeId);
                        var test = JsonSerializer.Serialize(recordVersion, typeof(MapDataForJson));
                        sw.Write(test);
                        sw.Write("," + Environment.NewLine);
                    }
                    md2 = null;
                    //Attempt to reduce memory usage faster so bigger files get processed faster
                    w1.nds = null;
                    w1.name = "";
                }
                Log.WriteLog("Standalone tagged ways converted to MapData at " + DateTime.Now);
                ways = ways.Where(w => referencedWays[w.id].Count() > 0).ToList();

                foreach (var r1 in osmRelations)
                {
                    var md3 = ProcessRelation(r1, ref ways);

                    if (md3 != null) //null can be returned from the functions that convert OSM entries to MapData
                    {
                        var recordVersion = new MapDataForJson(md3.name, md3.place.AsText(), md3.type, md3.WayId, md3.NodeId, md3.RelationId, md3.AreaTypeId);
                        var test = JsonSerializer.Serialize(recordVersion, typeof(MapDataForJson));
                        sw.Write(test);
                        sw.Write("," + Environment.NewLine);
                    }
                }
                Log.WriteLog("Relations converted to MapData at " + DateTime.Now);

                //this is a final check for entries that are an inner way in a relation that is also its own separate entity. (First pass would not have found it because it's referenced, 2nd pass missed because it's inner)
                var outerWays = osmRelations.SelectMany(r => r.Members.Where(m => m.Role == "outer" && m.Type == OsmGeoType.Way).Select(m => m.Id)).ToLookup(k => k, v => v);
                ways = ways.Where(w => outerWays[w.id].Count() == 0).ToList();
                outerWays = null;
                osmRelations = null;

                if (!writeDirectly)
                    processedEntries.AddRange(ways.AsParallel().Select(w => ConvertWayToMapData(ref w)));
                else
                {
                    foreach (var w3 in ways)
                    {
                        var w4 = w3;
                        var md4 = ConvertWayToMapData(ref w4);

                        if (md4 != null) //null can be returned from the functions that convert OSM entries to MapData
                        {
                            var recordVersion = new MapDataForJson(md4.name, md4.place.AsText(), md4.type, md4.WayId, md4.NodeId, md4.RelationId, md4.AreaTypeId);
                            var test = JsonSerializer.Serialize(recordVersion, typeof(MapDataForJson));
                            sw.Write(test);
                            sw.Write("," + Environment.NewLine);
                        }
                    }
                }
                ways = null;

                sw.Write("]");
                sw.Close();
                sw.Dispose();
                return null;
            }
        }

        private static List<MapData> ProcessData(ILookup<long, NodeReference> osmNodes, ref List<OsmSharp.Way> osmWays, ref List<OsmSharp.Relation> osmRelations, HashSet<long> referencedWays)
        {
            List<MapData> processedEntries = new List<MapData>();
            List<NodeData> nodes = new List<NodeData>();
            List<WayData> ways = new List<WayData>();

            nodes.Capacity = 100000;
            ways.Capacity = 100000;
            processedEntries.Capacity = 100000;

            //Write nodes as mapdata if they're tagged separately from other things.
            Log.WriteLog("Finding tagged nodes at " + DateTime.Now);
            var taggedNodes = osmNodes.AsParallel().Where(n => n.First().name != "" && n.First().type != "" && n.First().type != null).ToList();
            processedEntries.AddRange(taggedNodes.AsParallel().Select(s => MapSupport.ConvertNodeToMapData(s.First())));
            taggedNodes = null;

            Log.WriteLog("Converting " + osmWays.Count() + " OsmWays to my Ways at " + DateTime.Now);
            ways.Capacity = osmWays.Count();
            ways = osmWays.AsParallel().Select(w => new WayData()
            {
                id = w.Id.Value,
                name = GetElementName(w.Tags),
                AreaType = MapSupport.GetElementType(w.Tags),
                nodRefs = w.Nodes.ToList()
            })
            .ToList();
            osmWays = null; //free up RAM we won't use again.
            Log.WriteLog("List created at " + DateTime.Now);

            int wayCounter = 0;

            System.Threading.Tasks.Parallel.ForEach(ways, (w) =>
            //foreach(var w in ways)
            {
                wayCounter++;
                if (wayCounter % 10000 == 0)
                    Log.WriteLog(wayCounter + " processed so far");

                LoadNodesIntoWay(ref w, ref osmNodes);
            }
            );

            Log.WriteLog("Ways populated with Nodes at " + DateTime.Now);
            osmNodes = null; //done with these now, can free up RAM again.


            //Process all the ways that aren't used by a relation, then remove them.
            processedEntries.AddRange(ways.Where(w => !referencedWays.Contains(w.id)).AsParallel().Select(w => ConvertWayToMapData(ref w)));
            ways = ways.Where(w => referencedWays.Contains(w.id)).ToList();

            //processedEntries.AddRange(ProcessRelations(ref osmRelations, ref ways)); //75580ms on OH data
            processedEntries.AddRange(osmRelations.AsParallel().Select(r => ProcessRelation(r, ref ways))); //42223ms on OH.

            //Removed entries we've already looked at as part of a relation? I suspect this is one of those cases where
            //either way I do this, something's going to get messed up. Should track what.
            //Removing ways already in a reference screws up: Oak Grove Cemetery at BGSU
            //Re-processing ways already in a reference screws up:
            //ways = ways.Where(w => referencedWays[w.id].Count() == 0).ToList(); 
            //I might want to only remove outer ways, and let inner ways remain in case they're something else.
            var outerWays = osmRelations.SelectMany(r => r.Members.Where(m => m.Role == "outer" && m.Type == OsmGeoType.Way).Select(m => m.Id)).ToHashSet();
            ways = ways.Where(w => !outerWays.Contains(w.id)).ToList();
            outerWays = null;
            osmRelations = null;

            processedEntries.AddRange(ways.AsParallel().Select(w => ConvertWayToMapData(ref w)));
            ways = null;
            return processedEntries;
        }

        public static void LoadNodesIntoWay(ref WayData w, ref ILookup<long, NodeReference> osmNodes)
        {
            foreach (long nr in w.nodRefs)
            {
                var osmNode = osmNodes[nr].FirstOrDefault();
                var myNode = new NodeData(osmNode.Id, osmNode.lat, osmNode.lon);
                w.nds.Add(myNode);
            }
            w.nodRefs = null; //free up a little memory we won't use again?
        }

        private static List<MapData> ProcessRelations(ref List<OsmSharp.Relation> osmRelations, ref List<WayData> ways)
        {
            List<MapData> results = new List<MapData>();
            PraxisContext db = new PraxisContext();

            foreach (var r in osmRelations)
                results.Add(ProcessRelation(r, ref ways));

            return results;
        }

        private static MapData ProcessRelation(OsmSharp.Relation r, ref List<WayData> ways)
        {
            PraxisContext db = new PraxisContext();

            string relationName = GetElementName(r.Tags);
            //Determine if we need to process this relation.
            //if all ways are closed outer polygons, we can skip this.
            //if all ways are lines that connect, we need to make it a polygon.
            //We can't always rely on tags being correct.

            //I might need to check if these are usable ways before checking if they're already handled by the relation
            //Remove entries we won't use.

            var membersToRead = r.Members.Where(m => m.Type == OsmGeoType.Way).Select(m => m.Id).ToList();
            if (membersToRead.Count == 0)
            {
                Log.WriteLog("Relation " + r.Id + " " + relationName + " has no Ways, cannot process.");
                return null;
            }

            //Check members for closed shape
            var shapeList = new List<WayData>();
            foreach (var m in membersToRead)
            {
                var maybeWay = ways.Where(way => way.id == m).FirstOrDefault();
                if (maybeWay != null && maybeWay.nds.Count() >= 2) //2+ is a line, 1 is a point. I have relations with 2- and 3-point lines. THey're just not complete shapes.
                    shapeList.Add(maybeWay);
                else
                {
                    Log.WriteLog("Relation " + r.Id + " " + relationName + " references way " + m + " not found in the file. Attempting to process without it.", Log.VerbosityLevels.High);
                    //TODO: add some way of saving this partial data to the DB to be fixed/enhanced later?
                    //break;
                }
            }
            membersToRead = null;

            //Now we have our list of Ways. Check if there's lines that need made into a polygon.
            if (shapeList.Any(s => s.nds.Count == 0))
            {
                Log.WriteLog("Relation " + r.Id + " " + relationName + " has ways with 0 nodes.");
            }

            Geometry Tpoly = GetGeometryFromWays(shapeList, r);
            if (Tpoly == null)
            {
                //error converting it
                Log.WriteLog("Relation " + r.Id + " " + relationName + " failed to get a polygon from ways. Error.");
                return null;
            }

            if (!Tpoly.IsValid)
            {
                //System.Diagnostics.Debugger.Break();
                Log.WriteLog("Relation " + r.Id + " " + relationName + " Is not valid geometry. Error.");
                return null;
            }

            MapData md = new MapData();
            md.name = GetElementName(r.Tags);
            md.type = MapSupport.GetElementType(r.Tags);
            md.AreaTypeId = MapSupport.areaTypeReference[md.type.StartsWith("admin") ? "admin" : md.type].First();
            md.RelationId = r.Id.Value;
            md.place = MapSupport.SimplifyArea(Tpoly);
            if (md.place == null)
                return null;

            return md;
        }

        private static Geometry GetGeometryFromWays(List<WayData> shapeList, OsmSharp.Relation r)
        {
            //A common-ish case looks like the outer entries are lines that join togetehr, and inner entries are polygons.
            //Let's see if we can build a polygon (or more, possibly)
            List<Coordinate> possiblePolygon = new List<Coordinate>();
            //from the first line, find the line that starts with the same endpoint (or ends with the startpoint, but reverse that path).
            //continue until a line ends with the first node. That's a closed shape.

            List<Polygon> existingPols = new List<Polygon>();
            List<Polygon> innerPols = new List<Polygon>();

            if (shapeList.Count == 0)
            {
                Log.WriteLog("Relation " + r.Id + " " + GetElementName(r.Tags) + " has 0 ways in shapelist", Log.VerbosityLevels.High);
                return null;
            }

            //Separate sets
            var innerEntries = r.Members.Where(m => m.Role == "inner").Select(m => m.Id).ToList(); //these are almost always closed polygons.
            var outerEntries = r.Members.Where(m => m.Role == "outer").Select(m => m.Id).ToList();
            var innerPolys = new List<WayData>();

            if (innerEntries.Count() + outerEntries.Count() > shapeList.Count)
            {
                Log.WriteLog("Relation " + r.Id + " " + GetElementName(r.Tags) + " is missing Ways, odds of success are low.", Log.VerbosityLevels.High);
            }

            //Not all ways are tagged for this, so we can't always rely on this.
            if (outerEntries.Count > 0)
                shapeList = shapeList.Where(s => outerEntries.Contains(s.id)).ToList();

            if (innerEntries.Count > 0)
            {
                innerPolys = shapeList.Where(s => innerEntries.Contains(s.id)).ToList();
                //foreach (var ie in innerPolys)
                //{
                while (innerPolys.Count() > 0)
                    //TODO: confirm all of these are closed shapes.
                    innerPols.Add(GetShapeFromLines(ref innerPolys));
                //}
            }

            //Remove any closed shapes first from the outer entries.
            var closedShapes = shapeList.Where(s => s.nds.First().id == s.nds.Last().id).ToList();
            foreach (var cs in closedShapes)
            {
                if (cs.nds.Count() > 3) //TODO: if SimplifyAreas is true, this might have been a closedShape that became a linestring or point from this.
                {
                    shapeList.Remove(cs);
                    existingPols.Add(factory.CreatePolygon(cs.nds.Select(n => new Coordinate(n.lon, n.lat)).ToArray()));
                }
                else
                    Log.WriteLog("Invalid closed shape found: " + cs.id);
            }

            while (shapeList.Count() > 0)
                existingPols.Add(GetShapeFromLines(ref shapeList)); //only outers here.

            existingPols = existingPols.Where(e => e != null).ToList();

            if (existingPols.Count() == 0)
            {
                Log.WriteLog("Relation " + r.Id + " " + GetElementName(r.Tags) + " has no polygons and no lines that make polygons. Is this relation supposed to be an open line?", Log.VerbosityLevels.High);
                return null;
            }

            if (existingPols.Count() == 1)
            {
                //remove inner polygons
                var returnData = existingPols.First();
                foreach (var ir in innerPolys)
                {
                    if (ir.nds.First().id == ir.nds.Last().id)
                    {
                        var innerP = factory.CreateLineString(WayToCoordArray(ir));
                        returnData.InteriorRings.Append(innerP);
                    }
                }
                return returnData;
            }

            //return a multipolygon instead.
            Geometry multiPol = factory.CreateMultiPolygon(existingPols.Distinct().ToArray());
            //A new attempt at removing inner entries from outer ones via multipolygon.
            if (innerPols.Count() > 0)
            {
                var innerMultiPol = factory.CreateMultiPolygon(innerPols.Where(ip => ip != null).Distinct().ToArray());
                try
                {
                    multiPol = multiPol.Difference(innerMultiPol);
                }
                catch (Exception ex)
                {
                    Log.WriteLog("Relation " + r.Id + " Error trying to pull difference from inner and outer polygons:" + ex.Message);
                }
            }
            return multiPol;
        }
        private static Polygon GetShapeFromLines(ref List<WayData> shapeList)
        {
            //takes shapelist as ref, returns a polygon, leaves any other entries in shapelist to be called again.
            //NOTE/TODO: if this is a relation of lines that aren't a polygon (EX: a very long hiking trail), this should probably return the combined linestring?
            //TODO: if the lines are too small, should I return a Point instead?

            List<Coordinate> possiblePolygon = new List<Coordinate>();
            var firstShape = shapeList.FirstOrDefault();
            if (firstShape == null)
            {
                Log.WriteLog("shapelist has 0 ways in shapelist?", Log.VerbosityLevels.High);
                return null;
            }
            shapeList.Remove(firstShape);
            var nextStartnode = firstShape.nds.Last();
            var closedShape = false;
            var isError = false;
            possiblePolygon.AddRange(firstShape.nds.Where(n => n.id != nextStartnode.id).Select(n => new Coordinate(n.lon, n.lat)).ToList());
            while (closedShape == false)
            {
                var allPossibleLines = shapeList.Where(s => s.nds.First().id == nextStartnode.id).ToList();
                if (allPossibleLines.Count > 1)
                {
                    Log.WriteLog("Shape has multiple possible lines to follow, might not process correctly.", Log.VerbosityLevels.High);
                }
                var lineToAdd = shapeList.Where(s => s.nds.First().id == nextStartnode.id && s.nds.First().id != s.nds.Last().id).FirstOrDefault();
                if (lineToAdd == null)
                {
                    //check other direction
                    var allPossibleLinesReverse = shapeList.Where(s => s.nds.Last().id == nextStartnode.id).ToList();
                    if (allPossibleLinesReverse.Count > 1)
                    {
                        Log.WriteLog("Way has multiple possible lines to follow, might not process correctly (Reversed Order).");
                    }
                    lineToAdd = shapeList.Where(s => s.nds.Last().id == nextStartnode.id && s.nds.First().id != s.nds.Last().id).FirstOrDefault();
                    if (lineToAdd == null)
                    {
                        //If all lines are joined and none remain, this might just be a relation of lines. Return a combined element
                        Log.WriteLog("shape doesn't seem to have properly connecting lines, can't process as polygon.", Log.VerbosityLevels.High);
                        closedShape = true; //rename this to something better for breaking the loop
                        isError = true; //rename this to something like IsPolygon
                    }
                    else
                        lineToAdd.nds.Reverse();
                }
                if (!isError)
                {
                    possiblePolygon.AddRange(lineToAdd.nds.Where(n => n.id != nextStartnode.id).Select(n => new Coordinate(n.lon, n.lat)).ToList());
                    nextStartnode = lineToAdd.nds.Last();
                    shapeList.Remove(lineToAdd);

                    if (possiblePolygon.First().Equals(possiblePolygon.Last()))
                        closedShape = true;
                }
            }
            if (isError)
                return null;

            if (possiblePolygon.Count <= 3)
            {
                Log.WriteLog("Didn't find enough points to turn into a polygon. Probably an error.", Log.VerbosityLevels.High);
                return null;
            }

            var poly = factory.CreatePolygon(possiblePolygon.ToArray());
            poly = CCWCheck(poly);
            if (poly == null)
            {
                Log.WriteLog("Found a shape that isn't CCW either way. Error.", Log.VerbosityLevels.High);
                return null;
            }
            return poly;
        }

        private static void GetAllEntriesFromPbf(Stream dataStream, string areaType, out List<OsmSharp.Relation> relList, out List<OsmSharp.Way> wayList, out Dictionary<long, NodeReference> nodes, out List<MapData> results)
        {
            //Try and pull everything out of the file at once, instead of doing 3 passes on it.
            //This does assume, however, that everything is in order (All relations appear before a way they reference, and all ways appear before the nodes they reference.
            //This assumption may not be true, which would justify the 3-pass effort. This could cut it down to 2 passes (one for tagged stuff, one for referenced-and-not-tagged stuff)
            //Might also do processing here as a way to keep ram low-but-growing over time?
            //BAH THEYRE SORTED BACKWARDS.
            //Files have nodes first, then ways, then relations.
            //BUT
            //.ToComplete() gives me entries with all the members filled in, instead of doing the passes myself.
            List<OsmSharp.Relation> rs = new List<Relation>();
            List<OsmSharp.Way> ws = new List<Way>();
            Dictionary<long, NodeReference> ns = new Dictionary<long, NodeReference>();

            List<MapData> mds = new List<MapData>();

            //use these to track referenced entries internally, instead of externally. Can then also remove items from these.
            //THIS might be where I want to use a ConcurrentX collection instead of a baseline one, if i make this foreach parallel.
            HashSet<long> rels = new HashSet<long>();
            HashSet<long> ways = new HashSet<long>();
            HashSet<long> nods = new HashSet<long>();

            rs.Capacity = 100000;
            ws.Capacity = 100000;

            var source = new PBFOsmStreamSource(dataStream);
            var source2 = source.Where(s => MapSupport.GetElementType(s.Tags) != "" && s.Type == OsmGeoType.Relation).ToComplete();


            foreach (var entry in source2)
            {
                //switch(entry.Type)
                //{
                //    case OsmGeoType.Relation:
                //        if (MapSupport.GetElementType(entry.Tags) != "")
                //        {
                CompleteRelation temp = (CompleteRelation)entry;
                var t = Complete.ProcessCompleteRelation(temp);
                //I should make a function that processes this.

                //            foreach (var m in temp.Members.Where(m => m.Type == OsmGeoType.Way).Select(m => m.Id))
                //                ways.Add(m);
                //        }
                //        break;
                //    case OsmGeoType.Way:
                //        if (MapSupport.GetElementType(entry.Tags) != "" || ways.Contains(entry.Id))
                //        {
                //            Way temp = (Way)entry;
                //            ws.Add(temp);
                //            foreach (var m in temp.Nodes)
                //                nods.Add(m);
                //        }
                //        break;
                //    case OsmGeoType.Node:
                //        if (MapSupport.GetElementType(entry.Tags) != "" || nods.Contains(entry.Id))
                //        {
                //            var n = (OsmSharp.Node)entry;
                //            ns.Add(n.Id.Value, new NodeReference(n.Id.Value, (float)n.Latitude, (float)n.Longitude, GetElementName(n.Tags), MapSupport.GetElementType(n.Tags)));
                //        }
                //        break;
                //}
            }

            relList = rs;
            wayList = ws;
            nodes = ns;
            results = mds;
        }

        private static List<OsmSharp.Relation> GetRelationsFromPbf(string filename, string areaType)
        {
            //Read through a file for stuff that matches our parameters.
            List<OsmSharp.Relation> filteredRelations = new List<OsmSharp.Relation>();
            using (var fs = File.OpenRead(filename))
            {
                filteredRelations = InnerGetRelations(fs, areaType);
            }
            return filteredRelations;
        }

        private static List<OsmSharp.Relation> GetRelationsFromStream(Stream file, string areaType)
        {
            //Read through a file for stuff that matches our parameters.
            List<OsmSharp.Relation> filteredRelations = new List<OsmSharp.Relation>();
            file.Position = 0;
            return InnerGetRelations(file, areaType);
        }

        private static List<OsmSharp.Relation> InnerGetRelations(Stream stream, string areaType)
        {
            var source = new PBFOsmStreamSource(stream);
            var progress = source; //.ShowProgress();

            List<OsmSharp.Relation> filteredEntries;
            if (areaType == null)
                filteredEntries = progress.AsParallel().Where(p => p.Type == OsmGeoType.Relation &&
                    MapSupport.GetElementType(p.Tags) != "")
                .Select(p => (OsmSharp.Relation)p)
                .ToList();
            else if (areaType == "admin")
                filteredEntries = progress.AsParallel().Where(p => p.Type == OsmGeoType.Relation &&
                    MapSupport.GetElementType(p.Tags).StartsWith(areaType))
                .Select(p => (OsmSharp.Relation)p)
                .ToList();
            else
                filteredEntries = progress.AsParallel().Where(p => p.Type == OsmGeoType.Relation &&
                MapSupport.GetElementType(p.Tags) == areaType
            )
                .Select(p => (OsmSharp.Relation)p)
                .ToList();

            return filteredEntries;
        }

        private static List<OsmSharp.Way> GetWaysFromPbf(string filename, string areaType, ILookup<long, short> referencedWays)
        {
            //Read through a file for stuff that matches our parameters.
            List<OsmSharp.Way> filteredWays = new List<OsmSharp.Way>();
            using (var fs = File.OpenRead(filename))
            {
                filteredWays = InnerGetWays(fs, areaType, referencedWays);
            }
            return filteredWays;
        }

        private static List<OsmSharp.Way> GetWaysFromStream(Stream file, string areaType, ILookup<long, short> referencedWays)
        {
            //Read through a memorystream for stuff that matches our parameters.   
            file.Position = 0;
            return InnerGetWays(file, areaType, referencedWays);
        }

        private static List<OsmSharp.Way> GetWaysFromStream(Stream file, string areaType, HashSet<long> referencedWays)
        {
            //Read through a memorystream for stuff that matches our parameters.   
            file.Position = 0;
            return InnerGetWays(file, areaType, referencedWays);
        }

        private static List<OsmSharp.Way> InnerGetWays(Stream file, string areaType, ILookup<long, short> referencedWays)
        {
            List<OsmSharp.Way> filteredWays = new List<OsmSharp.Way>();
            var source = new PBFOsmStreamSource(file);
            var progress = source; //.ShowProgress();

            if (areaType == null)
            {
                filteredWays = progress.AsParallel().Where(p => p.Type == OsmGeoType.Way &&
                (MapSupport.GetElementType(p.Tags) != ""
                || referencedWays[p.Id.Value].Count() > 0)
            )
                .Select(p => (OsmSharp.Way)p)
                .ToList();
            }
            else if (areaType == "admin")
            {
                filteredWays = progress.AsParallel().Where(p => p.Type == OsmGeoType.Way &&
                    (MapSupport.GetElementType(p.Tags).StartsWith(areaType)
                    || referencedWays[p.Id.Value].Count() > 0)
                )
                    .Select(p => (OsmSharp.Way)p)
                    .ToList();
            }
            else
            {
                filteredWays = progress.AsParallel().Where(p => p.Type == OsmGeoType.Way &&
                    (MapSupport.GetElementType(p.Tags) == areaType
                    || referencedWays[p.Id.Value].Count() > 0)
                )
                    .Select(p => (OsmSharp.Way)p)
                    .ToList();
            }

            return filteredWays;
        }

        private static List<OsmSharp.Way> InnerGetWays(Stream file, string areaType, HashSet<long> referencedWays)
        {
            List<OsmSharp.Way> filteredWays = new List<OsmSharp.Way>();
            var source = new PBFOsmStreamSource(file);
            var progress = source; //.ShowProgress();

            if (areaType == null)
            {
                filteredWays = progress.AsParallel().Where(p => p.Type == OsmGeoType.Way &&
                (MapSupport.GetElementType(p.Tags) != ""
                || referencedWays.Contains(p.Id.Value))
            )
                .Select(p => (OsmSharp.Way)p)
                .ToList();
            }
            else if (areaType == "admin")
            {
                filteredWays = progress.AsParallel().Where(p => p.Type == OsmGeoType.Way &&
                    (MapSupport.GetElementType(p.Tags).StartsWith(areaType)
                    || referencedWays.Contains(p.Id.Value))
                )
                    .Select(p => (OsmSharp.Way)p)
                    .ToList();
            }
            else
            {
                filteredWays = progress.AsParallel().Where(p => p.Type == OsmGeoType.Way &&
                    (MapSupport.GetElementType(p.Tags) == areaType
                    || referencedWays.Contains(p.Id.Value))
                )
                    .Select(p => (OsmSharp.Way)p)
                    .ToList();
            }

            return filteredWays;
        }

        private static ILookup<long, NodeReference> GetNodesFromPbf(string filename, string areaType, ILookup<long, short> nodes)
        {
            ILookup<long, NodeReference> filteredEntries;
            using (var fs = File.OpenRead(filename))
            {
                filteredEntries = InnerGetNodes(fs, areaType, nodes);
            }
            return filteredEntries;
        }

        private static ILookup<long, NodeReference> GetNodesFromStream(Stream file, string areaType, ILookup<long, short> nodes)
        {
            file.Position = 0;
            return InnerGetNodes(file, areaType, nodes);
        }

        private static ILookup<long, NodeReference> GetNodesFromStream(Stream file, string areaType, HashSet<long> nodes)
        {
            file.Position = 0;
            return InnerGetNodes(file, areaType, nodes);
        }

        public static ILookup<long, NodeReference> InnerGetNodes(Stream file, string areaType, ILookup<long, short> nodes)
        {
            var source = new PBFOsmStreamSource(file);
            var progress = source; //.ShowProgress();
            ILookup<long, NodeReference> filteredEntries;

            if (areaType == null)
            {
                filteredEntries = progress.AsParallel().Where(p => p.Type == OsmGeoType.Node &&
               (MapSupport.GetElementType(p.Tags) != "" || nodes[p.Id.Value].Count() > 0)
           )
               .Select(n => new NodeReference(n.Id.Value, (float)((OsmSharp.Node)n).Latitude.Value, (float)((OsmSharp.Node)n).Longitude.Value, GetElementName(n.Tags), MapSupport.GetElementType(n.Tags)))
               .ToLookup(k => k.Id, v => v);
            }
            else if (areaType == "admin")
            {
                filteredEntries = progress.AsParallel().Where(p => p.Type == OsmGeoType.Node &&
               (MapSupport.GetElementType(p.Tags).StartsWith(areaType) || nodes[p.Id.Value].Count() > 0)
            )
               .Select(n => new NodeReference(n.Id.Value, (float)((OsmSharp.Node)n).Latitude.Value, (float)((OsmSharp.Node)n).Longitude.Value, GetElementName(n.Tags), areaType))
               .ToLookup(k => k.Id, v => v);
            }
            else
            {
                filteredEntries = progress.AsParallel().Where(p => p.Type == OsmGeoType.Node &&
                   (MapSupport.GetElementType(p.Tags) == areaType || nodes.Contains(p.Id.Value)) //might use less CPU than [].count() TODO test/determine if true.
               )
                   .Select(n => new NodeReference(n.Id.Value, (float)((OsmSharp.Node)n).Latitude.Value, (float)((OsmSharp.Node)n).Longitude.Value, GetElementName(n.Tags), areaType))
                   .ToLookup(k => k.Id, v => v);
            }

            return filteredEntries;
        }

        public static ILookup<long, NodeReference> InnerGetNodes(Stream file, string areaType, HashSet<long> nodes)
        {
            var source = new PBFOsmStreamSource(file);
            var progress = source; //.ShowProgress();
            ILookup<long, NodeReference> filteredEntries;

            if (areaType == null)
            {
                filteredEntries = progress.AsParallel().Where(p => p.Type == OsmGeoType.Node &&
               (MapSupport.GetElementType(p.Tags) != "" || nodes.Contains(p.Id.Value))
           )
               .Select(n => new NodeReference(n.Id.Value, (float)((OsmSharp.Node)n).Latitude.Value, (float)((OsmSharp.Node)n).Longitude.Value, GetElementName(n.Tags), MapSupport.GetElementType(n.Tags)))
               .ToLookup(k => k.Id, v => v);
            }
            else if (areaType == "admin")
            {
                filteredEntries = progress.AsParallel().Where(p => p.Type == OsmGeoType.Node &&
               (MapSupport.GetElementType(p.Tags).StartsWith(areaType) || nodes.Contains(p.Id.Value))
            )
               .Select(n => new NodeReference(n.Id.Value, (float)((OsmSharp.Node)n).Latitude.Value, (float)((OsmSharp.Node)n).Longitude.Value, GetElementName(n.Tags), areaType))
               .ToLookup(k => k.Id, v => v);
            }
            else
            {
                filteredEntries = progress.AsParallel().Where(p => p.Type == OsmGeoType.Node &&
                   (MapSupport.GetElementType(p.Tags) == areaType || nodes.Contains(p.Id.Value))
               )
                   .Select(n => new NodeReference(n.Id.Value, (float)((OsmSharp.Node)n).Latitude.Value, (float)((OsmSharp.Node)n).Longitude.Value, GetElementName(n.Tags), areaType))
                   .ToLookup(k => k.Id, v => v);
            }

            return filteredEntries;
        }

        public static void WriteMapDataToFile(string filename, ref List<MapData> mapdata)
        {
            //TODO: i could probably parallelize the .Serialize() part by doing some kind of AsParallel.Select() on it.
            //but StreamWriter needs to be written from one thread. Remember that if I change this.
            System.IO.StreamWriter sw = new StreamWriter(filename);
            sw.Write("[" + Environment.NewLine);
            foreach (var md in mapdata)
            {
                if (md != null) //null can be returned from the functions that convert OSM entries to MapData
                {
                    var recordVersion = new MapDataForJson(md.name, md.place.AsText(), md.type, md.WayId, md.NodeId, md.RelationId, md.AreaTypeId);
                    var test = JsonSerializer.Serialize(recordVersion, typeof(MapDataForJson));
                    sw.Write(test);
                    sw.Write("," + Environment.NewLine);
                }
            }
            sw.Write("]");
            sw.Close();
            sw.Dispose();
            Log.WriteLog("All MapData entries were serialized individually and saved to file at " + DateTime.Now);
        }

        public static List<MapData> ReadMapDataToMemory(string filename)
        {
            //Got out of memory errors trying to read files over 1GB through File.ReadAllText, so do those here this way.
            StreamReader sr = new StreamReader(filename);
            List<MapData> lm = new List<MapData>();
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
                    MapDataForJson j = (MapDataForJson)JsonSerializer.Deserialize(line.Substring(0, line.Count() - 1), typeof(MapDataForJson), jso);
                    var temp = new MapData() { name = j.name, NodeId = j.NodeId, place = reader.Read(j.place), RelationId = j.RelationId, type = j.type, WayId = j.WayId, AreaTypeId = j.AreaTypeId }; //first entry on a file before I forced the brackets onto newlines. Comma at end causes errors, is also trimmed.
                    if (temp.place is Polygon)
                    {
                        temp.place = MapSupport.CCWCheck((Polygon)temp.place);
                    }
                    if (temp.place is MultiPolygon)
                    {
                        MultiPolygon mp = (MultiPolygon)temp.place;
                        for (int i = 0; i < mp.Geometries.Count(); i++)
                        {
                            mp.Geometries[i] = CCWCheck((Polygon)mp.Geometries[i]);
                        }
                        temp.place = mp;
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

        public static void RemoveDuplicates()
        {
            //I might need to reconsider how i handle duplicates, since different files will have different pieces of some ways.
            //Current plan: process relations bigger than the files I normally use separately from the larger files, store those in their own file.
            Log.WriteLog("Scanning for duplicate entries at " + DateTime.Now);
            var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var dupedMapDatas = db.MapData.Where(md => md.WayId != null).GroupBy(md => md.WayId)
                .Select(m => new { m.Key, Count = m.Count() })
                .ToDictionary(d => d.Key, v => v.Count)
                .Where(md => md.Value > 1);
            Log.WriteLog("Duped MapData loaded at " + DateTime.Now);

            foreach (var dupe in dupedMapDatas)
            {
                var entriesToDelete = db.MapData.Where(md => md.WayId == dupe.Key).ToList();
                db.MapData.RemoveRange(entriesToDelete.Skip(1));
                db.SaveChanges(); //so the app can make partial progress if it needs to restart
            }
            db.SaveChanges();
            Log.WriteLog("Duped MapData entries deleted at " + DateTime.Now);
        }

        public static void CreateStandaloneDB(long relationID, string parentFile)
        {
            //TODO: this whole feature.
            //Parameter: RelationID?
            //pull in all ways that intersect that 
            //process all of the 10-cells inside that area with their ways (will need more types than the current game has)
            //save this data to an SQLite DB for the app to use.

            var mainDb = new PraxisContext();
            var sqliteDb = "placeholder";
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values. //share this here, so i compare the actual algorithms instead of this boilerplate, mandatory entry.
            //var relationToProcess = 

            //var polygon = factory.CreatePolygon(MapSupport.GeoAreaToCoordArray(box));

            var source = new PBFOsmStreamSource(File.OpenRead(parentFile));
            //Using different types than my existing functions.
            //first, pull the relation out of the parent file.
            var relation = (Relation)source.Where(s => s.Id == relationID && s.Type == OsmGeoType.Relation).FirstOrDefault();
            if (relation == null)
                return; //not in the parent file. TODO log.

            var relationMemberIds = relation.Members.Select(m => m.Id).ToList();
            //var 

            //GeoAPI.Geometries.Coordinate[] coords = new GeoAPI.Geometries.Coordinate[] {
            //    new GeoAPI.Geometries.Coordinate(box.Min.Longitude, box.Min.Latitude),
            //    new GeoAPI.Geometries.Coordinate(box.Min.Longitude, box.Max.Latitude),
            //    new GeoAPI.Geometries.Coordinate(box.Max.Longitude, box.Max.Latitude),
            //    new GeoAPI.Geometries.Coordinate(box.Max.Longitude, box.Min.Latitude),
            //    new GeoAPI.Geometries.Coordinate(box.Min.Longitude, box.Min.Latitude),
            //};

            //var dataFromParent = source.FilterBox((float)box.Min.Longitude, (float)box.Max.Latitude, (float)box.Max.Longitude, (float)box.Min.Longitude).ToList();

            //var nodes = dataFromParent.Where(d => d.Type == OsmGeoType.Node).ToList();
            //var ways = dataFromParent.Where(d => d.Type == OsmGeoType.Way).ToList();
            //var relations = dataFromParent.Where(d => d.Type == OsmGeoType.Relation).ToList();
            //dataFromParent = null;

            //could now sort by tags/types. This version should track more types than the explore game, since this is a more detailed smaller area.



            //var content = mainDb.MapData.Where(md => md.place.Intersects(polygon)).ToList();
            //var spoiConent = mainDb.SinglePointsOfInterests.Where(s => polygon.Intersects(new Point(s.lon, s.lat))).ToList(); //I think this is the right function, but might be Covers?

            //now, convert everything in content to 10-char plus code data.
        }

        public static void ValidateFile(string filename)
        {
            //Ohio.pbf results: 
            //Validate a PBF file
            //List entries that can or cannot be processed

            Log.WriteLog("Checking File " + filename + " at " + DateTime.Now);

            List<OsmSharp.Relation> rs = new List<OsmSharp.Relation>();
            List<OsmSharp.Way> ws = new List<OsmSharp.Way>();
            List<OsmSharp.Node> ns = new List<OsmSharp.Node>();

            rs.Capacity = 1000000;
            ws.Capacity = 1000000;
            ns.Capacity = 1000000;

            using (var fs = File.OpenRead(filename))
            {
                var source = new PBFOsmStreamSource(fs);
                var progress = source.ShowProgress();

                foreach (var entry in progress)
                {
                    if (entry.Type == OsmGeoType.Node)
                        ns.Add((OsmSharp.Node)entry);
                    else if (entry.Type == OsmGeoType.Way)
                        ws.Add((OsmSharp.Way)entry);
                    else if (entry.Type == OsmGeoType.Relation)
                        rs.Add((OsmSharp.Relation)entry);
                }
            }

            Log.WriteLog("Entries pulled into Memory at " + DateTime.Now);

            var rL = rs.ToLookup(k => k.Id, v => v);
            var wL = ws.ToLookup(k => k.Id, v => v);
            var nL = ns.ToLookup(k => k.Id, v => v);
            rs = null;
            ws = null;
            ns = null;

            Log.WriteLog("Lookups create at " + DateTime.Now);

            List<long> badRelations = new List<long>();
            List<long> badWays = new List<long>();

            bool gotoNext = false;
            foreach (var key in rL)
            {
                foreach (var r in key)
                {
                    gotoNext = false;
                    foreach (var m in r.Members)
                    {
                        if (gotoNext)
                            continue;
                        if (m.Type == OsmGeoType.Way && wL[m.Id].Count() > 0)
                        { } //OK
                        else
                        {
                            Log.WriteLog("Relation " + r.Id + "  " + GetElementName(r.Tags) + " is missing Way " + m.Id);
                            badRelations.Add(r.Id.Value);
                            gotoNext = true;
                            continue;
                        }
                    }
                }
            }

            Log.WriteLog("Total of " + badRelations.Count() + " unusable relations in a set of " + rL.Count());
        }

        public static void ExtractAreasFromLargeFile(string filename)
        {
            //This should refer to a list of relations that cross multiple extract files, to get a more accurate set of data in game.
            //Starting with North America, will test later on global data
            //Should start with big things
            //Great lakes, major rivers, some huge national parks. Oceans are important for global data.
            //Rough math suggests that this will take 103 minutes to skim planet-latest.osm.pbf per pass.
            //Takes ~17 minutes per pass the 'standard' way on north-america-latest.

            string outputFile = ParserSettings.JsonMapDataFolder + "LargeAreas" + (ParserSettings.UseHighAccuracy ? "-highAcc" : "-lowAcc") + ".json";

            var manualRelationId = new List<long>() {
                //Great Lakes:
                4039900, //Lake Erie
                1205151, //Lake Huron
                1206310, //lake ontario
                1205149, //lake michigan --not valid geometry?
                4039486, //lake superior
                //Admin boundaries:
                148838, //US Admin bounds
                9331155, //48 Contiguous US states
                1428125, //Canada
                //EU?
                //Which other countries do I have divided down into state/provinces?
                //UK
                //Germany
                //France
                //Russia
                //others
                //TODO: add oceans. these might not actually exist as a single entry in OSM. Will have to check why
                //TODO: multi-state or multi-nation rivers.
                2182501, //Ohio River
                1756854, //Mississippi river --failed to get a polygon?
                //other places:
                //yellowstone?
                //grand canyon?
            };

            //Might want to pass an option for MemoryStream on this, since I can store the 7GB continent file in RAM but not the 54GB Planet file.
            var stream = new FileStream(filename, FileMode.Open);
            var source = new PBFOsmStreamSource(stream);
            File.Delete(outputFile); //Clear out any existing entries.

            File.AppendAllLines(outputFile, new List<String>() { "[" });
            var rs = source.Where(s => s.Type == OsmGeoType.Relation && manualRelationId.Contains(s.Id.Value)).Select(s => (OsmSharp.Relation)s).ToList();
            Log.WriteLog("Relation data pass completed at " + DateTime.Now);
            List<WayData> ways = new List<WayData>();
            var referencedWays = rs.SelectMany(r => r.Members.Where(m => m.Type == OsmGeoType.Way).Select(m => m.Id)).Distinct().ToLookup(k => k, v => v);
            var ways2 = source.Where(s => s.Type == OsmGeoType.Way && referencedWays[s.Id.Value].Count() > 0).Select(s => (OsmSharp.Way)s).ToList();
            var referencedNodes = ways2.SelectMany(m => m.Nodes).Distinct().ToLookup(k => k, v => v);
            var nodes2 = source.Where(s => s.Type == OsmGeoType.Node && referencedNodes[s.Id.Value].Count() > 0).Select(n => new NodeReference(n.Id.Value, (float)((OsmSharp.Node)n).Latitude.Value, (float)((OsmSharp.Node)n).Longitude.Value, GetElementName(n.Tags), MapSupport.GetElementType(n.Tags))).ToList();
            Log.WriteLog("Relevant data pulled from file at" + DateTime.Now);

            var osmNodeLookup = nodes2.AsParallel().ToLookup(k => k.Id, v => v);
            Log.WriteLog("Found " + osmNodeLookup.Count() + " unique nodes");

            ways.Capacity = ways2.Count();
            ways = ways2.AsParallel().Select(w => new WayData()
            {
                id = w.Id.Value,
                name = GetElementName(w.Tags),
                AreaType = MapSupport.GetElementType(w.Tags),
                nodRefs = w.Nodes.ToList()
            })
            .ToList();
            ways2 = null; //free up RAM we won't use again.
            Log.WriteLog("List created at " + DateTime.Now);

            int wayCounter = 0;
            System.Threading.Tasks.Parallel.ForEach(ways, (w) =>
            {
                wayCounter++;
                if (wayCounter % 10000 == 0)
                    Log.WriteLog(wayCounter + " processed so far");

                LoadNodesIntoWay(ref w, ref osmNodeLookup);
            });

            Log.WriteLog("Ways populated with Nodes at " + DateTime.Now);
            nodes2 = null; //done with these now, can free up RAM again.
            var mapDataEntries = ProcessRelations(ref rs, ref ways);

            //convert to jsonmapdata type
            foreach (var mapDataEntry in mapDataEntries)
            {
                if (mapDataEntry != null)
                {
                    MapDataForJson output = new MapDataForJson(mapDataEntry.name, mapDataEntry.place.AsText(), mapDataEntry.type, mapDataEntry.WayId, mapDataEntry.NodeId, mapDataEntry.RelationId, mapDataEntry.AreaTypeId);
                    File.AppendAllLines(outputFile, new List<String>() { JsonSerializer.Serialize(output, typeof(MapDataForJson)) + "," });
                }
            }
            File.AppendAllLines(outputFile, new List<String>() { "]" });
        }

        public static void ScanMapForDullAreas(GeoArea fullAreaToScan)
        {
            //TOOD: this function
            //Identify which places on a map are boring.
            //Current plan:
            //If a Cell8 entry contains only uninteresting entries (roads, building, parking lots. Area ID > 12 is the current simple check) 
            //then mark that area as 'dull', and use that list later to create zones

            //NOTE; this is getting moved to on-demand to save time setting up a new DB.

            //split area into Cell8 entries

            //save list of Cell8 entries that need something in them.
            List<string> dullCell8s = new List<string>();


        }

        public static void SingleTest()
        {
            //trying to find one relation to fix.
            string filename = ParserSettings.PbfFolder + "ohio-latest.osm.pbf";

            List<NodeData> nodes = new List<NodeData>();
            List<WayData> ways = new List<WayData>();
            List<MapData> processedEntries = new List<MapData>();
            //Minimizes time spend boosting capacity and copying the internal values later.
            nodes.Capacity = 100000;
            ways.Capacity = 100000;
            processedEntries.Capacity = 100000;

            long oneId = 6113131;

            FileStream fs = new FileStream(filename, FileMode.Open);
            var source = new PBFOsmStreamSource(fs);
            var relation = source.Where(s => s.Type == OsmGeoType.Relation && s.Id == oneId).Select(s => (OsmSharp.Relation)s).ToList(); //should be a list of 1
            var referencedWays = relation.SelectMany(r => r.Members.Where(m => m.Type == OsmGeoType.Way).Select(m => m.Id)).Distinct().ToLookup(k => k, v => v);
            var ways2 = source.Where(s => s.Type == OsmGeoType.Way && referencedWays[s.Id.Value].Count() > 0).Select(s => (OsmSharp.Way)s).ToList();
            var referencedNodes = ways2.SelectMany(m => m.Nodes).Distinct().ToLookup(k => k, v => v);
            var nodes2 = source.Where(s => s.Type == OsmGeoType.Node && referencedNodes[s.Id.Value].Count() > 0).Select(n => new NodeReference(n.Id.Value, (float)((OsmSharp.Node)n).Latitude.Value, (float)((OsmSharp.Node)n).Longitude.Value, GetElementName(n.Tags), MapSupport.GetElementType(n.Tags))).ToList();
            Log.WriteLog("Relevant data pulled from file at" + DateTime.Now);

            //Log.WriteLog("Creating node lookup for " + osmNodes.Count() + " nodes"); //33 million nodes across 2 million ways will tank this app at 16GB RAM
            var osmNodeLookup = nodes2.AsParallel().ToLookup(k => k.Id, v => v);
            Log.WriteLog("Found " + osmNodeLookup.Count() + " unique nodes");

            //Write nodes as mapdata if they're tagged separately from other things.
            Log.WriteLog("Finding tagged nodes at " + DateTime.Now);
            var taggedNodes = nodes2.AsParallel().Where(n => n.name != "" && n.type != "").ToList();
            processedEntries.AddRange(taggedNodes.Select(s => MapSupport.ConvertNodeToMapData(s)));
            taggedNodes = null;

            //This is now the slowest part of the processing function.
            Log.WriteLog("Converting " + ways2.Count() + " OsmWays to my Ways at " + DateTime.Now);
            ways.Capacity = ways2.Count();
            ways = ways2.AsParallel().Select(w => new WayData()
            {
                id = w.Id.Value,
                name = GetElementName(w.Tags),
                AreaType = MapSupport.GetElementType(w.Tags),
                nodRefs = w.Nodes.ToList()
            })
            .ToList();
            ways2 = null; //free up RAM we won't use again.
            Log.WriteLog("List created at " + DateTime.Now);

            int wayCounter = 0;
            //foreach (WayData w in ways)
            System.Threading.Tasks.Parallel.ForEach(ways, (w) =>
            {
                wayCounter++;
                if (wayCounter % 10000 == 0)
                    Log.WriteLog(wayCounter + " processed so far");

                LoadNodesIntoWay(ref w, ref osmNodeLookup);
            });

            Log.WriteLog("Ways populated with Nodes at " + DateTime.Now);
            nodes2 = null; //done with these now, can free up RAM again.

            processedEntries.AddRange(ProcessRelations(ref relation, ref ways));

            processedEntries.AddRange(ways.Select(w => ConvertWayToMapData(ref w)));
            ways = null;

            string destFileName = System.IO.Path.GetFileNameWithoutExtension(filename);
            WriteMapDataToFile(ParserSettings.JsonMapDataFolder + destFileName + "-MapData-Test.json", ref processedEntries);
            AddMapDataToDBFromFiles();

        }

        /* For reference: the tags Pokemon Go appears to be using. I don't need all of these. I have a few it doesn't, as well.
         * POkemon Go is using these as map tiles, not just content. This is not primarily a maptile app.
    KIND_BASIN
    KIND_CANAL
    KIND_CEMETERY - Have
    KIND_CINEMA
    KIND_COLLEGE - Have
    KIND_COMMERCIAL
    KIND_COMMON
    KIND_DAM
    KIND_DITCH
    KIND_DOCK
    KIND_DRAIN
    KIND_FARM
    KIND_FARMLAND
    KIND_FARMYARD
    KIND_FOOTWAY -Have
    KIND_FOREST
    KIND_GARDEN
    KIND_GLACIER
    KIND_GOLF_COURSE
    KIND_GRASS
    KIND_HIGHWAY -have
    KIND_HOSPITAL
    KIND_HOTEL
    KIND_INDUSTRIAL
    KIND_LAKE -have, as water
    KIND_LAND
    KIND_LIBRARY
    KIND_MAJOR_ROAD - have
    KIND_MEADOW
    KIND_MINOR_ROAD - have
    KIND_NATURE_RESERVE - Have
    KIND_OCEAN - have, as water
    KIND_PARK - Have
    KIND_PARKING - have
    KIND_PATH - have, as trail
    KIND_PEDESTRIAN
    KIND_PITCH
    KIND_PLACE_OF_WORSHIP
    KIND_PLAYA
    KIND_PLAYGROUND
    KIND_QUARRY
    KIND_RAILWAY
    KIND_RECREATION_AREA
    KIND_RESERVOIR
    KIND_RETAIL - Have
    KIND_RIVER - have, as water
    KIND_RIVERBANK - have, as water
    KIND_RUNWAY
    KIND_SCHOOL
    KIND_SPORTS_CENTER
    KIND_STADIUM
    KIND_STREAM - have, as water
    KIND_TAXIWAY
    KIND_THEATRE
    KIND_UNIVERSITY - Have
    KIND_URBAN_AREA
    KIND_WATER - Have
    KIND_WETLAND - Have
    KIND_WOOD
         */

        /*
         * and for reference, the Google Maps Playable Locations valid types (Interaction points, not terrain types?)
         *  education
            entertainment
            finance
            food_and_drink
            outdoor_recreation
            retail
            tourism
            transit
            transportation_infrastructure
            wellness
         */
    }
}
