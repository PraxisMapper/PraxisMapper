using CoreComponents;
using CoreComponents.Support;
using Google.OpenLocationCode;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Features;
using OsmSharp;
using OsmSharp.Geo;
using OsmSharp.Streams;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using static CoreComponents.ConstantValues;
using static CoreComponents.DbTables;
using static CoreComponents.Place;
using static CoreComponents.Singletons;
using static CoreComponents.TagParser;
using SkiaSharp;

//TODO: look into using Span<T> instead of lists? This might be worth looking at performance differences. (and/or Memory<T>, which might be a parent for Spans)
//TODO: Ponder using https://download.bbbike.org/osm/ as a data source to get a custom extract of an area (for when users want a local-focused app, probably via a wizard GUI)
//OR could use an additional input for filterbox.

namespace Larry
{
    class Program
    {
        static void Main(string[] args)
        {
            var memMon = new MemoryMonitor();
            PraxisContext.connectionString = ParserSettings.DbConnectionString;
            PraxisContext.serverMode = ParserSettings.DbMode;

            if (args.Count() == 0)
            {
                Console.WriteLine("You must pass an arguement to this application");
                //TODO: list valid commands or point at the docs file
                return;
            }

            if (args.Any(a => a.StartsWith("-dbMode:")))
            {
                //scan a file for information on what will or won't load.
                string arg = args.Where(a => a.StartsWith("-dbMode:")).First().Replace("-dbMode:", "");
                PraxisContext.serverMode = arg;
            }

            if (args.Any(a => a.StartsWith("-dbConString:")))
            {
                //scan a file for information on what will or won't load.
                string arg = args.Where(a => a.StartsWith("-dbConString:")).First().Replace("-dbConString:", "");
                PraxisContext.connectionString = arg;
            }

            //Check for settings flags first before running any commands.
            if (args.Any(a => a == "-v" || a == "-verbose"))
                Log.Verbosity = Log.VerbosityLevels.High;

            if (args.Any(a => a == "-noLogs"))
                Log.Verbosity = Log.VerbosityLevels.Off;

            if (args.Any(a => a == "-skipRoadsAndBuildings"))
            {
                DbSettings.processRoads = false;
                DbSettings.processBuildings = false;
                DbSettings.processParking = false;
            }

            if (args.Any(a => a == "-spaceSaver"))
            {
                ParserSettings.UseHighAccuracy = false;
                factory = NtsGeometryServices.Instance.CreateGeometryFactory(new PrecisionModel(1000000), 4326); //SRID matches Plus code values.  Precision model means round all points to 7 decimal places to not exceed float's useful range.
                SimplifyAreas = true; //rounds off points that are within a Cell10's distance of each other. Makes fancy architecture and highly detailed paths less pretty on map tiles, but works for gameplay data.
            }

            //If multiple args are supplied, run them in the order that make sense, not the order the args are supplied.

            if (args.Any(a => a == "-createDB")) //setup the destination database
            {
                Console.WriteLine("Creating database with current database settings.");
                DBCommands.MakePraxisDB();
            }

            if (args.Any(a => a == "-cleanDB"))
            {
                Console.WriteLine("Clearing out tables for testing.");
                DBCommands.CleanDb();
            }

            if (args.Any(a => a == "-findServerBounds"))
            {
                DBCommands.FindServerBounds();
            }

            if (args.Any(a => a == "-singleTest"))
            {
                //Check on a specific thing. Not an end-user command.
                //Current task: Identify issue with relation
                SingleTest();
            }

            if (args.Any(a => a.StartsWith("-getPbf:")))
            {
                //Wants 3 pieces. Drops in placeholders if some are missing. Giving no parameters downloads Ohio.
                string arg = args.Where(a => a.StartsWith("-getPbf:")).First().Replace("-getPbf:", "");
                var splitData = arg.Split('|'); //remember the first one will be empty.
                string level1 = splitData.Count() >= 4 ? splitData[3] : "north-america";
                string level2 = splitData.Count() >= 3 ? splitData[2] : "us";
                string level3 = splitData.Count() >= 2 ? splitData[1] : "ohio";

                DownloadPbfFile(level1, level2, level3, ParserSettings.PbfFolder);
            }

            if (args.Any(a => a == "-resetXml" || a == "-resetPbf")) //update both anyways.
            {
                FileCommands.ResetFiles(ParserSettings.PbfFolder);
            }

            if (args.Any(a => a == "-resetJson"))
            {
                FileCommands.ResetFiles(ParserSettings.JsonMapDataFolder);
            }

            if (args.Any(a => a == "-trimPbfFiles"))
            {
                FileCommands.MakeAllSerializedFilesFromPBF();
            }

            if (args.Any(a => a.StartsWith("-trimPbfsByType")))
            {
                List<string> filenames = System.IO.Directory.EnumerateFiles(ParserSettings.PbfFolder, "*.pbf").ToList();
                foreach (string filename in filenames)
                    PbfOperations.SerializeSeparateFilesFromPBF(filename);
            }

            if (args.Any(a => a.StartsWith("-lastChance")))
            {
                //split this arg
                var areaType = args.Where(a => a.StartsWith("-lastChance")).First().Split(":")[1];
                List<string> filenames = System.IO.Directory.EnumerateFiles(ParserSettings.PbfFolder, "*.pbf").ToList();
                foreach (string filename in filenames)
                    PbfOperations.LastChanceSerializer(filename, areaType);
            }

            if (args.Any(a => a == "-readMapData"))
            {
                DBCommands.AddMapDataToDBFromFiles();
            }

            if (args.Any(a => a == "-updateDatabase"))
            {
                DBCommands.UpdateExistingEntries();
            }

            if (args.Any(a => a == "-removeDupes"))
            {
                DBCommands.RemoveDuplicates();
            }

            if (args.Any(a => a.StartsWith("-createStandalone")))
            {
                //This makes a standalone DB for a specific relation passed in as a paramter. Originally used a geoArea, now calculates that from a Relation.
                //If you think you want an area, it's probably better to pick an admin boundary as a relation that covers the area.

                int relationId = args.Where(a => a.StartsWith("-createStandalone")).First().Split('|')[1].ToInt();
                CreateStandaloneDB(relationId, false, true); //How map tiles are handled is determined by the optional parameters
            }

            if (args.Any(a => a.StartsWith("-checkFile:")))
            {
                //scan a file for information on what will or won't load.
                string arg = args.Where(a => a.StartsWith("-checkFile:")).First().Replace("-checkFile:", "");
                PbfOperations.ValidateFile(arg);
            }

            if (args.Any(a => a == "-autoCreateMapTiles")) //better for letting the app decide which tiles to create than manually calling out Cell6 names.
            {
                //NOTE: this loop ran at 11 maptiles per second on my original attempt. This optimized setup runs at up to 2600 maptiles per second.
                //Remember: this shouldn't draw GeneratedMapTile areas, nor should this create them.
                //Tiles should be redrawn when those get made, if they get made.
                //This should also over-write existing map tiles if present, in case the data's been updated since last run.
                //TODO: add logic to either overwrite or skip existing tiles.
                bool skip = true; //This skips over 128,000 tiles in about a minute. Decent.

                //Potential alternative idea:
                //One loops detects which map tiles need drawn, using the algorithm, and saves that list to a new DB table
                //A second process digs through that list and draws the map tiles, then marks them  as drawn (or deletes them from the list?)

                //Search for all areas that needs a map tile created.
                List<string> Cell2s = new List<string>();

                //Cell2 detection loop: 22-CV. All others are 22-XX.
                for (var pos1 = 0; pos1 <= OpenLocationCode.CodeAlphabet.IndexOf('C'); pos1++)
                    for (var pos2 = 0; pos2 <= OpenLocationCode.CodeAlphabet.IndexOf('V'); pos2++)
                    {
                        string cellToCheck = OpenLocationCode.CodeAlphabet[pos1].ToString() + OpenLocationCode.CodeAlphabet[pos2].ToString();
                        var area = new OpenLocationCode(cellToCheck);
                        var tileNeedsMade = DoPlacesExist(area.Decode());
                        if (tileNeedsMade)
                        {
                            Log.WriteLog("Noting: Cell2 " + cellToCheck + " has areas to draw");
                            Cell2s.Add(cellToCheck);
                        }
                        else
                        {
                            Log.WriteLog("Skipping Cell2 " + cellToCheck + " for future mapdrawing checks.");
                        }
                    }

                foreach (var cell2 in Cell2s)
                    DetectMapTilesRecursive(cell2, skip);
            }

            if (args.Any(a => a == "-extractBigAreas"))
            {
                PbfOperations.ExtractAreasFromLargeFile(ParserSettings.PbfFolder + "planet-latest.osm.pbf"); //Guarenteed to have everything. Estimates 400+ minutes per run, including loading country boundaries
                //PbfOperations.ExtractAreasFromLargeFile(ParserSettings.PbfFolder + "north-america-latest.osm.pbf");
            }

            if (args.Any(a => a == "-fixAreaSizes"))
            {
                DBCommands.FixAreaSizes();
            }


            if (args.Any(a => a.StartsWith("-populateEmptyArea:")))
            {
                var db = new PraxisContext();
                var cell6 = args.Where(a => a.StartsWith("-populateEmptyArea:")).First().Split(":")[1];
                CodeArea box6 = OpenLocationCode.DecodeValid(cell6);
                var location6 = Converters.GeoAreaToPolygon(box6);
                var places = db.MapData.Where(md => md.place.Intersects(location6) && md.AreaTypeId < 13).ToList();
                var fakeplaces = db.GeneratedMapData.Where(md => md.place.Intersects(location6)).ToList();

                for (int x = 0; x < 20; x++)
                {
                    for (int y = 0; y < 20; y++)
                    {
                        string cell8 = cell6 + OpenLocationCode.CodeAlphabet[x] + OpenLocationCode.CodeAlphabet[y];
                        CodeArea box = OpenLocationCode.DecodeValid(cell8);
                        var location = Converters.GeoAreaToPolygon(box);
                        if (!places.Any(md => md.place.Intersects(location)) && !fakeplaces.Any(md => md.place.Intersects(location)))
                            CreateInterestingPlaces(cell8);
                    }
                }
            }

            //This logic path probably isn't necessary, with importV4 replacing its logic of 'save everything now, draw at runtime'
            if (args.Any(a => a.StartsWith("-testDbDump")))
            {
                PbfRawDump.DumpToDb(ParserSettings.PbfFolder + "delaware-latest.osm.pbf"); //Delaware is the smallest state.
            }

            if (args.Any(a => a.StartsWith("-importV4")))
            {
                // 4th generation of logic for importing OSM data from PBF file.
                //V4 rules:
                //Tags will be saved to a separate table. (this lets me update formatting rules without reimporting data).
                //All entries  are processed into the database as geometry objects into one table.
                //Use the built-in Feature converter in OsmSharp.Geo instead of maintaining the OSM-to-Geometry logic myself.
                //Only nodes that match the tag rules will be imported as their own entries. Untagged nodes will be ignored.
                //attempt to stream data to avoid memory issues. MIght need to do multiple filters on small areas (degree square? half-degree?)

                //I need to avoid adding duplicate entries, and i dont want to do huge passes per entry added. 
                //Is there an 'ignore on duplicate' command or setting in MariaDB and SQL Server? I might need to set those. CAn it be the default behavior?

                TagParser.Initialize();
                //load data. Testing with delaware for speed again.
                string pbfFilename = ParserSettings.PbfFolder + "delaware-latest.osm.pbf";
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

                //TODO: make a variant of this that writes the data in this square degree to a file, then splits off
                //a new Task<> to process that file instead of reading it from the main file. This would let me use more thread
                //and more RAM, like a big server environment.
                if (!testMultithreaded)
                {
                    for (var i = minWest; i < maxEast; i++)
                        for (var j = minsouth; j < maxNorth; j++)
                        {
                            var loadedRelations = ProcessDegreeAreaV4(j, i, pbfFilename); //This happens to be a 4-digit PlusCode, being 1 degree square.
                            foreach (var lr in loadedRelations)
                                relationsToSkip.Add(lr);
                        }
                }
                else
                {
                    //This is the multi-thread variant. TODO: test this logic.
                    List<string> tempFiles = new List<string>();
                    List<Task<List<long>>> taskStatuses = new List<Task<List<long>>>();
                    for (var i = minWest; i < maxEast; i++)
                        for (var j = minsouth; j < maxNorth; j++)
                        {
                            fs = File.OpenRead(pbfFilename);
                            source = new PBFOsmStreamSource(fs);
                            var filtered = source.FilterBox(i, j + 1, i + 1, j, true);
                            string tempFile = "tempFile-" + i + "-" + j + ".pbf";
                            using (var destFile = new FileInfo(tempFile).Open(FileMode.Create))
                            {
                                tempFiles.Add(tempFile);
                                var target = new PBFOsmStreamTarget(destFile);
                                target.RegisterSource(filtered);
                                target.Pull();
                            }
                            Task<List<long>> process = Task.Run(() => ProcessDegreeAreaV4(j, i, tempFile));
                            taskStatuses.Add(process);
                        }
                    Task.WaitAll(taskStatuses.ToArray());
                    foreach (var t in taskStatuses)
                    {
                        foreach (var id in t.Result)
                            relationsToSkip.Add(id);
                    }
                    //end multithread variant.
                    ///Oh, dont forget to delete the temp files.
                    foreach (var tf in tempFiles)
                        File.Delete(tf);
                }


                //special pass for missed elements. Some things, like Delaware Bay, don't show up on this process
                //(Why isn't clear but its some OsmSharp behavior. Relations that aren't entirely contained in the filter area are excluded, I think?)
                Log.WriteLog("Attempting to reload missed elements...");
                fs = System.IO.File.OpenRead(pbfFilename);
                source = new PBFOsmStreamSource(fs);
                PraxisContext db = new PraxisContext();
                var secondChance = source.ToComplete().Where(s => s.Type == OsmGeoType.Relation && !relationsToSkip.Contains(s.Id)); //logic change - only load relations we haven't tried yet.
                foreach(var sc in secondChance)
                {
                    var found = GeometrySupport.ConvertOsmEntryToStoredWay(sc);
                    if (found != null)
                    {
                        db.StoredWays.Add(found);
                    }
                }
                Log.WriteLog("Saving final data....");
                db.SaveChanges();
                Log.WriteLog("Final pass completed at " + DateTime.Now);
            }
        }


        public static List<long> ProcessDegreeAreaV4(float south, float west, string filename)
        {
            List<long> failedRelations = new List<long>(); //return this, so the parent function knows what to look for in a full-pass.

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
            System.Threading.ReaderWriterLockSlim locker = new System.Threading.ReaderWriterLockSlim();

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
                    failedRelations.Add(r.Id);
                    var convertedRelation = GeometrySupport.ConvertOsmEntryToStoredWay(r);
                    if (convertedRelation == null)
                    {
                        continue;
                    }

                    db.StoredWays.Add(convertedRelation);
                    totalItems++;
                    relationCounter++;
                    if (relationCounter > 100)
                    {
                        difference = DateTime.Now - startedProcess;
                        double percentage = (totalCounter / totalRelations) * 100;
                        var entriesPerSecond = totalCounter / difference.TotalSeconds;
                        var secondsLeft = (totalRelations - totalCounter) / entriesPerSecond;
                        TimeSpan estimatedTime = TimeSpan.FromSeconds(secondsLeft);
                        Log.WriteLog(Math.Round(entriesPerSecond) + " Relations per second processed, " + Math.Round(percentage, 2) + "% done, estimated time remaining: " + estimatedTime.ToString());
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

                    db.StoredWays.Add(item);
                    if (wayCounter > 10000)
                    {
                        difference = DateTime.Now - startedProcess;
                        double percentage = (totalCounter / totalWays) * 100;
                        var entriesPerSecond = totalCounter / difference.TotalSeconds;
                        var secondsLeft = (totalWays - totalCounter) / entriesPerSecond;
                        TimeSpan estimatedTime = TimeSpan.FromSeconds(secondsLeft);
                        Log.WriteLog(Math.Round(entriesPerSecond) + " Ways per second processed, " + Math.Round(percentage, 2) + "% done, estimated time remaining: " + estimatedTime.ToString());
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
                    Log.WriteLog("Node Data Loaded");
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

                    db.StoredWays.Add(item);
                    if (nodeCounter > 10000)
                    {
                        difference = DateTime.Now - startedProcess;
                        double percentage = (totalCounter / totalnodes) * 100;
                        var entriesPerSecond = totalCounter / difference.TotalSeconds;
                        var secondsLeft = (totalnodes - totalCounter) / entriesPerSecond;
                        TimeSpan estimatedTime = TimeSpan.FromSeconds(secondsLeft);
                        Log.WriteLog(Math.Round(entriesPerSecond) + " Nodes per second processed, " + Math.Round(percentage, 2) + "% done, estimated time remaining: " + estimatedTime.ToString());
                        wayCounter = 0;
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

            Log.WriteLog("Saving " + totalItems + " entries to the database.....");
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            db.SaveChanges();
            sw.Stop();
            Log.WriteLog("Saving complete in " + sw.Elapsed + ".");

            source.Dispose(); source = null;
            fs.Close(); fs.Dispose(); fs = null;

            return failedRelations;
        }

        

        public static void DetectMapTilesRecursive(string parentCell, bool skipExisting) //This was off slightly at one point, but I didn't document how much or why. Should be correct now.
        {
            List<string> cellsFound = new List<string>();
            List<MapTile> tilesGenerated = new List<MapTile>(400); //Might need to be a ConcurrentBag or something similar?
                                                                   //Dictionary<string, string> existingTiles = new Dictionary<string, string>();
                                                                   //if (parentCell.Length == 6 && skipExisting)
                                                                   //{
                                                                   //var db = new PraxisContext();
                                                                   //existingTiles = db.MapTiles.Where(m => m.PlusCode.StartsWith(parentCell)).Select(m => m.PlusCode).ToDictionary<string, string>(k => k);
                                                                   //db.Dispose();
                                                                   //db = null;
                                                                   //}

            if (parentCell.Length == 4)
            {
                var checkProgress = new PraxisContext();
                bool skip = checkProgress.TileTrackings.Any(tt => tt.PlusCodeCompleted == parentCell);
                checkProgress.Dispose();
                checkProgress = null;
                if (skip)
                    return;
            }

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();

            //ConcurrentBag<MapData>cell6Data = new ConcurrentBag<MapData>();
            List<MapData> cell6Data = new List<MapData>();
            if (parentCell.Length == 6)
            {
                var area = OpenLocationCode.DecodeValid(parentCell);
                var areaPoly = Converters.GeoAreaToPolygon(area);
                var tempPlaces = GetPlaces(area, null, false, false);
                foreach (var t in tempPlaces)
                {
                    t.place = t.place.Intersection(areaPoly);
                    cell6Data.Add(t);
                }

            }

            //This is fairly well optimized, and I suspect there's not much more I can do here to get this to go faster.
            //using 2 parallel loops is faster than 1 or 0. Having MariaDB on the same box is what pegs the CPU, not this double-parallel loop.
            System.Threading.Tasks.Parallel.For(0, 20, (pos1) =>
            //for (int pos1 = 0; pos1 < 20; pos1++)
                System.Threading.Tasks.Parallel.For(0, 20, (pos2) =>
                //for (int pos2 = 0; pos2 < 20; pos2++)
                {
                    string cellToCheck = parentCell + OpenLocationCode.CodeAlphabet[pos1].ToString() + OpenLocationCode.CodeAlphabet[pos2].ToString();
                    var area = new OpenLocationCode(cellToCheck).Decode();
                    if (cellToCheck.Length == 8) //We don't want to do the DoPlacesExist check here, since we'll want empty tiles for empty areas at this l
                    {
                        var places = GetPlaces(area, cell6Data, false, false, 0); //These are cloned in GetPlaces, so we aren't intersecting areas twice and breaking drawing.
                        var tileData = MapTiles.DrawAreaMapTileSkia(ref places, area, 11); //now Skia drawing, should be faster. Peaks around 2600/s. ImageSharp peaked at 1600/s.
                        tilesGenerated.Add(new MapTile() { CreatedOn = DateTime.Now, mode = 1, tileData = tileData, resolutionScale = 11, PlusCode = cellToCheck });
                        Log.WriteLog("Cell " + cellToCheck + " Drawn", Log.VerbosityLevels.High);
                    }
                    else
                    {
                        var tileNeedsMade = DoPlacesExist(area);
                        if (tileNeedsMade)
                        {
                            Log.WriteLog("Noting: Cell" + cellToCheck.Length + " " + cellToCheck + " has areas to draw");
                            cellsFound.Add(cellToCheck);
                        }
                        else
                        {
                            Log.WriteLog("Skipping Cell" + cellToCheck.Length + " " + cellToCheck + " for future mapdrawing checks.", Log.VerbosityLevels.High);
                        }
                    }
                }));
            if (tilesGenerated.Count() > 0)
            {
                var db = new PraxisContext();
                db.MapTiles.AddRange(tilesGenerated);
                db.SaveChanges(); //This should run for every Cell6, saving up to 400 per batch.
                db.Dispose(); //connection needs terminated since this is recursive, or we will hit a max connections error eventually.
                db = null;
                Log.WriteLog("Saved records for Cell " + parentCell + " - " + tilesGenerated.Count() + " maptiles drawn and saved in " + sw.Elapsed.ToString());
            }
            foreach (var cellF in cellsFound)
                DetectMapTilesRecursive(cellF, skipExisting);

            if (parentCell.Length == 4)
            {
                var saveProgress = new PraxisContext();
                saveProgress.TileTrackings.Add(new TileTracking() { PlusCodeCompleted = parentCell });
                saveProgress.SaveChanges();
                saveProgress.Dispose();
                saveProgress = null;
                Log.WriteLog("Saved records for Cell4 " + parentCell + " in " + sw.Elapsed.ToString());
            }
        }

        //mostly-complete function for making files for a standalone app.
        public static void CreateStandaloneDB(long relationID, bool saveToDB = false, bool saveToFolder = true)
        {
            //TODO: add a parmeter to determine if maptiles are saved in the DB or separately in a folder.            
            //TODO in practice:
            //Set up area name to be area type  if name is blank.
            //

            var mainDb = new PraxisContext();
            var sqliteDb = new StandaloneContext(relationID.ToString());// "placeholder";
            sqliteDb.Database.EnsureCreated();

            var fullArea = mainDb.MapData.Where(m => m.RelationId == relationID).FirstOrDefault();
            if (fullArea == null)
                return;

            //Add a Cell8's worth of space to the edges of the area.
            GeoArea buffered = new GeoArea(fullArea.place.EnvelopeInternal.MinY, fullArea.place.EnvelopeInternal.MinX, fullArea.place.EnvelopeInternal.MaxY, fullArea.place.EnvelopeInternal.MaxX);
            //var intersectCheck = Converters.GeoAreaToPreparedPolygon(buffered);
            var intersectCheck = Converters.GeoAreaToPolygon(buffered); //Cant use prepared geometry against the db directly

            var allPlaces = mainDb.MapData.Where(md => intersectCheck.Intersects(md.place)).ToList();

            //now we have the list of places we need to be concerned with. 
            //start drawing maptiles and sorting out data.
            var swCorner = new OpenLocationCode(intersectCheck.EnvelopeInternal.MinY, intersectCheck.EnvelopeInternal.MinX);
            var neCorner = new OpenLocationCode(intersectCheck.EnvelopeInternal.MaxY, intersectCheck.EnvelopeInternal.MaxX);

            System.IO.Directory.CreateDirectory(relationID + "Tiles");
            //now, for every Cell8 involved, draw and name it.
            for (var y = swCorner.Decode().SouthLatitude; y <= neCorner.Decode().NorthLatitude; y += resolutionCell8)
            {
                for (var x = swCorner.Decode().WestLongitude; x <= neCorner.Decode().EastLongitude; x += resolutionCell8)
                {
                    //make map tile.
                    var plusCode = new OpenLocationCode(y, x, 10);
                    var areaForTile = new GeoArea(new GeoPoint(y, x), new GeoPoint(y + resolutionCell8, x + resolutionCell8));
                    var acheck2 = Converters.GeoAreaToPolygon(areaForTile);
                    var areaList = allPlaces.Where(a => a.place.Intersects(acheck2)).Select(a => a.Clone()).ToList();

                    System.Text.StringBuilder terrainInfo = AreaTypeInfo.SearchArea(ref areaForTile, ref areaList, true);
                    var splitData = terrainInfo.ToString().Split(Environment.NewLine);
                    foreach (var sd in splitData)
                    {
                        if (sd == "") //last result is always a blank line
                            continue;

                        var subParts = sd.Split('|'); //PlusCode|Name|AreaTypeID|MapDataID
                        sqliteDb.TerrainInfo.Add(new TerrainInfo() { Name = subParts[1], areaType = subParts[2].ToInt(), PlusCode = subParts[0], MapDataID = subParts[3].ToInt() });
                    }

                    var tile = MapTiles.DrawAreaMapTileSkia(ref areaList, areaForTile, 11);

                    if (saveToDB) //Some apps will prefer a single self-contained database file
                        sqliteDb.MapTiles.Add(new MapTileDB() { image = tile, layer = 1, PlusCode = plusCode.CodeDigits.Substring(0, 8) });

                    if (saveToFolder) //some apps, like my Solar2D apps, can't use the byte[] in a DB row and need files.
                        System.IO.File.WriteAllBytes(relationID + "Tiles\\" + plusCode.CodeDigits.Substring(0, 8) + ".pngTile", tile); //Solar2d also can't load pngs directly from an apk file in android, but the rule is extension based.
                }
            }
            sqliteDb.SaveChanges();

            //insert default entries.
            sqliteDb.PlayerStats.Add(new PlayerStats() { timePlayed = 0, distanceWalked = 0 });
            sqliteDb.Bounds.Add(new Bounds() { EastBound = neCorner.Decode().EastLongitude, NorthBound = neCorner.Decode().NorthLatitude, SouthBound = swCorner.Decode().SouthLatitude, WestBound = swCorner.Decode().WestLongitude });
            sqliteDb.SaveChanges();
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
            var nodes2 = source.Where(s => s.Type == OsmGeoType.Node && referencedNodes[s.Id.Value].Count() > 0).Select(n => new NodeReference(n.Id.Value, (float)((OsmSharp.Node)n).Latitude.Value, (float)((OsmSharp.Node)n).Longitude.Value, GetPlaceName(n.Tags), GetPlaceType(n.Tags))).ToList();
            Log.WriteLog("Relevant data pulled from file at" + DateTime.Now);

            //Log.WriteLog("Creating node lookup for " + osmNodes.Count() + " nodes"); //33 million nodes across 2 million ways will tank this app at 16GB RAM
            var osmNodeLookup = nodes2.AsParallel().ToLookup(k => k.Id, v => v);
            Log.WriteLog("Found " + osmNodeLookup.Count() + " unique nodes");

            //Write nodes as mapdata if they're tagged separately from other things.
            Log.WriteLog("Finding tagged nodes at " + DateTime.Now);
            var taggedNodes = nodes2.AsParallel().Where(n => n.name != "" && n.type != "").ToList();
            processedEntries.AddRange(taggedNodes.Select(s => Converters.ConvertNodeToMapData(s)));
            taggedNodes = null;

            //This is now the slowest part of the processing function.
            Log.WriteLog("Converting " + ways2.Count() + " OsmWays to my Ways at " + DateTime.Now);
            ways.Capacity = ways2.Count();
            ways = ways2.AsParallel().Select(w => new WayData()
            {
                id = w.Id.Value,
                name = GetPlaceName(w.Tags),
                AreaType = GetPlaceType(w.Tags),
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

                PbfOperations.LoadNodesIntoWay(ref w, ref osmNodeLookup);
            });

            Log.WriteLog("Ways populated with Nodes at " + DateTime.Now);
            nodes2 = null; //done with these now, can free up RAM again.

            processedEntries.AddRange(PbfOperations.ProcessRelations(ref relation, ref ways));

            processedEntries.AddRange(ways.Select(w => Converters.ConvertWayToMapData(ref w)));
            ways = null;

            string destFileName = System.IO.Path.GetFileNameWithoutExtension(filename);
            FileCommands.WriteMapDataToFile(ParserSettings.JsonMapDataFolder + destFileName + "-MapData-Test.json", ref processedEntries);
            DBCommands.AddMapDataToDBFromFiles();
        }

        public static void DownloadPbfFile(string topLevel, string subLevel1, string subLevel2, string destinationFolder)
        {
            //pull a fresh copy of a file from geofabrik.de (or other mirror potentially)
            //save it to the same folder as configured for pbf files (might be passed in)
            //web paths http://download.geofabrik.de/north-america/us/ohio-latest.osm.pbf
            //root, then each parent division. Starting with USA isn't too hard.
            //TODO: set this up to get files with different sub-level counts.
            var wc = new WebClient();
            wc.DownloadFile("http://download.geofabrik.de/" + topLevel + "/" + subLevel1 + "/" + subLevel2 + "-latest.osm.pbf", destinationFolder + subLevel2 + "-latest.osm.pbf");
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
