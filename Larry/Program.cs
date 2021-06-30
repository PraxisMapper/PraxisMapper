using CoreComponents;
using CoreComponents.Support;
using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using OsmSharp;
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
using static CoreComponents.StandaloneDbTables;
using CoreComponents.PbfReader;
using System.Text.Json;
using Pomelo.EntityFrameworkCore.MySql.Query.Internal;

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
                var db = new PraxisContext();
                db.MakePraxisDB();
            }

            if (args.Any(a => a == "-cleanDB"))
            {
                Console.WriteLine("Clearing out tables for testing.");
                DBCommands.CleanDb();
            }

            TagParser.Initialize(true); //Do this after the DB values are parsed.

            if (args.Any(a => a == "-findServerBounds"))
            {
                DBCommands.FindServerBounds();
            }

            if (args.Any(a => a == "-singleTest"))
            {
                //Check on a specific thing. Not an end-user command.
                //Current task: Identify issue with relation
                //SingleTest();
                new PbfReader().debugPerfTest(@"C:\praxis\ohio-latest.osm.pbf");
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

            if (args.Any(a => a == "-debugArea"))
            {
                var filename = ParserSettings.PbfFolder + "ohio-latest.osm.pbf";
                var areaId = 350381;
                PbfReader r = new PbfReader();
                r.debugArea(filename, areaId);
            }

            if (args.Any(a => a == "-loadPbfsToDb"))
            {
                List<string> filenames = System.IO.Directory.EnumerateFiles(ParserSettings.PbfFolder, "*.pbf").ToList();
                foreach (string filename in filenames)
                {
                    Log.WriteLog("Loading " + filename + " to database  at " + DateTime.Now);
                    PbfReader r = new PbfReader();
                    r.ProcessFile(filename, true);

                    File.Move(filename, filename + "done");
                    Log.WriteLog("Finished " + filename + " load at " + DateTime.Now);
                }
            }

            if (args.Any(a => a == "-loadPbfsToJson"))
            {
                List<string> filenames = System.IO.Directory.EnumerateFiles(ParserSettings.PbfFolder, "*.pbf").ToList();
                foreach (string filename in filenames)
                {
                    Log.WriteLog("Loading " + filename + " to JSON file at " + DateTime.Now);
                    PbfReader r = new PbfReader();
                    r.outputPath = ParserSettings.JsonMapDataFolder;
                    r.ProcessFile(filename);
                    File.Move(filename, filename + "done");
                }
            }

            if (args.Any(a => a == "-loadPbfsToJsonTest"))
            {
                List<string> filenames = System.IO.Directory.EnumerateFiles(ParserSettings.PbfFolder, "*.pbf").ToList();
                foreach (string filename in filenames)
                {

                    Log.WriteLog("Test Loading " + filename + " to JSON file at " + DateTime.Now);
                    PbfReader r = new PbfReader();
                    r.outputPath = ParserSettings.JsonMapDataFolder;
                    r.ProcessFile(filename);
                    File.Move(filename, filename + "done");
                    Log.WriteLog("Finished loaded " + filename + " to JSON at " + DateTime.Now);
                }
            }

            if (args.Any(a => a == "-convertJsonToSql"))
            {

                //test code
                var db = new PraxisContext();
                var entries = db.StoredOsmElements.Take(100).ToList();
                //var recordVersion = entries.Select(md =>  new StoredOsmElementForJson(md.id, md.name, md.sourceItemID, md.sourceItemType, md.elementGeometry.AsText(), string.Join("~", md.Tags.Select(t => t.Key + "|" + t.Value)), md.IsGameElement, md.IsUserProvided, md.IsGenerated)).ToList();
                //var test = recordVersion.Select(rv => JsonSerializer.Serialize(rv, typeof(StoredOsmElementForJson))).ToList();
                SqlExporter.DumpToSql(entries, "testfile.sql");


                //flip a JSON file to a SQL file and try and run it directly on the DB.
                //var db = new PraxisContext();
                //db.ChangeTracker.AutoDetectChangesEnabled = false;
                //List<string> filenames = System.IO.Directory.EnumerateFiles(ParserSettings.JsonMapDataFolder, "*.json").ToList();
                //long entryCounter = 0;
                //foreach (var jsonFileName in filenames)
                //{

                //    SqlExporter.DumpToSql();
                //}

            }

            if (args.Any(a => a == "-loadJsonToDb"))
            {
                var db = new PraxisContext();
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                List<string> filenames = System.IO.Directory.EnumerateFiles(ParserSettings.JsonMapDataFolder, "*.json").ToList();
                long entryCounter = 0;
                foreach (var jsonFileName in filenames)
                {
                    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                    Log.WriteLog("Loading " + jsonFileName + " to database at " + DateTime.Now);
                    var fr = File.OpenRead(jsonFileName);
                    var sr = new StreamReader(fr);
                    sw.Start();
                    while (!sr.EndOfStream)
                    {
                        //NOTE: the slow part here is inserting into the DB. 
                        //TODO: split off Tasks to convert these so the StreamReader doesn't have to wait on the converter/DB for progress
                        //SR only hits .9MB/s for this task, and these are multigigabyte files,
                        //But watch out for multithreading gotchas like usual.
                        //Would be: string = sr.Readline(); task -> convertStoredElement(string); lock and add to shared collection; after 100,000 entries lock then add collection to db and save changes.
                        //await all tasks once end of stream is hit. lock and add last elements to DB
                        StoredOsmElement stored = GeometrySupport.ConvertSingleJsonStoredElement(sr.ReadLine());
                        db.StoredOsmElements.Add(stored);
                        entryCounter++;

                        if (entryCounter > 100000)
                        {
                            Log.WriteLog("Started saving 100k entries after " + sw.Elapsed); //~11 seconds to get here of processing.
                            db.SaveChanges();
                            entryCounter = 0;
                            //This limits the RAM creep you'd see from adding 3 million rows at a time.
                            db = new PraxisContext();
                            db.ChangeTracker.AutoDetectChangesEnabled = false;
                            Log.WriteLog("100,000 entries processed to DB in " + sw.Elapsed);
                            sw.Restart();
                        }
                    }
                    sr.Close(); sr.Dispose();
                    fr.Close(); fr.Dispose();
                    db.SaveChanges();
                    File.Move(jsonFileName, jsonFileName + "done");
                }
            }

            //Testing a multithreaded version of this process.
            if (args.Any(a => a == "-loadJsonToDbTasks"))
            {
                var db = new PraxisContext();
                //db.ChangeTracker.AutoDetectChangesEnabled = false;
                List<string> filenames = System.IO.Directory.EnumerateFiles(ParserSettings.JsonMapDataFolder, "*.json").ToList();
                long entryCounter = 0;
                System.Threading.ReaderWriterLockSlim fileLock = new System.Threading.ReaderWriterLockSlim();
                foreach (var jsonFileName in filenames)
                {
                    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                    Log.WriteLog("Loading " + jsonFileName + " to database via tasks at " + DateTime.Now);
                    var fr = File.OpenRead(jsonFileName);
                    var sr = new StreamReader(fr);
                    sw.Start();
                    //System.Collections.Concurrent.ConcurrentBag<StoredOsmElement> templist = new System.Collections.Concurrent.ConcurrentBag<StoredOsmElement>();
                    List<StoredOsmElement> templist = new List<StoredOsmElement>();
                    List<Task> taskList = new List<Task>();
                    while (!sr.EndOfStream)
                    {
                        //TODO: split off Tasks to convert these so the StreamReader doesn't have to wait on the converter/DB for progress
                        //SR only hits .9MB/s for this task, and these are multigigabyte files
                        //But watch out for multithreading gotchas like usual.
                        //Would be: string = sr.Readline(); task -> convertStoredElement(string); lock and add to shared collection; after 10,000 entries lock then add collection to db and save changes.
                        //await all tasks once end of stream is hit. lock and add last elements to DB
                        string entry = sr.ReadLine();
                        var nextTask = Task.Run(() =>
                        {
                            StoredOsmElement stored = GeometrySupport.ConvertSingleJsonStoredElement(entry);
                            //fileLock.EnterReadLock();
                            templist.Add(stored);
                            //fileLock.ExitReadLock();
                            entryCounter++;
                            if (entryCounter > 100000)
                            {
                                //fileLock.EnterWriteLock();
                                //db.StoredOsmElements.AddRange(templist);

                                //db.SaveChanges();
                                entryCounter = 0;
                                //templist = new List<StoredOsmElement>();
                                //templist.Clear();
                                //This limits the RAM creep you'd see from adding 3 million rows at a time.
                                //db = new PraxisContext();
                                //db.ChangeTracker.AutoDetectChangesEnabled = false;
                                Log.WriteLog("100,000 entries processed for loading in " + sw.Elapsed);
                                sw.Restart();
                                //fileLock.ExitWriteLock();
                            }
                        });
                        taskList.Add(nextTask);
                    }
                    sr.Close(); sr.Dispose();
                    fr.Close(); fr.Dispose();
                    Log.WriteLog("File read to memory, waiting for tasks to complete....");

                    Task.WaitAll(taskList.ToArray());
                    Log.WriteLog("All elements converted in memory, loading to DB....");
                    taskList.Clear(); taskList = null;

                    //process the elements in batches. This might be where spans are a good idea?
                    var total = templist.Count();
                    int loopCount = 0;
                    int loopAmount = 10000;
                    while (loopAmount * loopCount < total)
                    {
                        sw.Restart();
                        db = new PraxisContext();
                        db.ChangeTracker.AutoDetectChangesEnabled = false;
                        var toAdd = templist.Skip(loopCount * loopAmount).Take(loopAmount);
                        db.StoredOsmElements.AddRange(toAdd);
                        db.SaveChanges();
                        Log.WriteLog(loopAmount + " entries saved to DB in " + sw.Elapsed);
                        loopCount += 1;
                    }

                    //db.StoredOsmElements.AddRange(templist);
                    //db.SaveChanges();
                    Log.WriteLog("Final entries for " + jsonFileName + " completed at " + DateTime.Now);


                    //db.SaveChanges();
                    File.Move(jsonFileName, jsonFileName + "done");
                }
            }

            if (args.Any(a => a == "-updateDatabase"))
            {
                DBCommands.UpdateExistingEntries();
            }

            if (args.Any(a => a == "-removeDupes"))
            {
                DBCommands.RemoveDuplicates();
            }

            if (args.Any(a => a.StartsWith("-createStandaloneRelation")))
            {
                //This makes a standalone DB for a specific relation passed in as a paramter. 
                int relationId = args.Where(a => a.StartsWith("-createStandaloneRelation")).First().Split('|')[1].ToInt();
                CreateStandaloneDB(relationId, null, false, true); //How map tiles are handled is determined by the optional parameters
            }

            if (args.Any(a => a.StartsWith("-createStandaloneBox")))
            {
                //This makes a standalone DB for a specific area passed in as a paramter.
                //If you want to cover a region in a less-specific way, or the best available relation is much larger than you thought, this might be better.

                string[] bounds = args.Where(a => a.StartsWith("-createStandaloneBox")).First().Split('|');
                GeoArea boundsArea = new GeoArea(bounds[1].ToDouble(), bounds[2].ToDouble(), bounds[3].ToDouble(), bounds[4].ToDouble());

                //in order, these go south/west/north/east.
                CreateStandaloneDB(0, boundsArea, false, true); //How map tiles are handled is determined by the optional parameters
            }

            if (args.Any(a => a.StartsWith("-createStandalonePoint")))
            {
                //This makes a standalone DB centered on a specific point, it will grab a Cell6's area around that point.
                string[] bounds = args.Where(a => a.StartsWith("-createStandalonePoint")).First().Split('|');

                var resSplit = resolutionCell6 / 2;
                GeoArea boundsArea = new GeoArea(bounds[1].ToDouble() - resSplit, bounds[2].ToDouble() - resSplit, bounds[1].ToDouble() + resSplit, bounds[2].ToDouble() + resSplit);

                //in order, these go south/west/north/east.
                CreateStandaloneDB(0, boundsArea, false, true); //How map tiles are handled is determined by the optional parameters
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

            if (args.Any(a => a.StartsWith("-drawOneImage:")))
            {
                string code = args.First(a => a.StartsWith("-drawOneImage:")).Split(":")[1];
                System.IO.File.WriteAllBytes(code + ".png", MapTiles.DrawPlusCode(code));
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
                var places = db.StoredOsmElements.Where(md => md.elementGeometry.Intersects(location6)).ToList(); //TODO: filter this down to only areas with IsGameElement == true
                var fakeplaces = places.Where(p => p.IsGenerated).ToList();

                for (int x = 0; x < 20; x++)
                {
                    for (int y = 0; y < 20; y++)
                    {
                        string cell8 = cell6 + OpenLocationCode.CodeAlphabet[x] + OpenLocationCode.CodeAlphabet[y];
                        CodeArea box = OpenLocationCode.DecodeValid(cell8);
                        var location = Converters.GeoAreaToPolygon(box);
                        if (!places.Any(md => md.elementGeometry.Intersects(location)) && !fakeplaces.Any(md => md.elementGeometry.Intersects(location)))
                            CreateInterestingPlaces(cell8);
                    }
                }
            }
        }

        public static void DetectMapTilesRecursive(string parentCell, bool skipExisting) //This was off slightly at one point, but I didn't document how much or why. Should be correct now.
        {
            List<string> cellsFound = new List<string>();
            List<MapTile> tilesGenerated = new List<MapTile>(400); //Might need to be a ConcurrentBag or something similar?

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
            List<StoredOsmElement> cell6Data = new List<StoredOsmElement>();
            if (parentCell.Length == 6)
            {
                var area = OpenLocationCode.DecodeValid(parentCell);
                var areaPoly = Converters.GeoAreaToPolygon(area);
                var tempPlaces = GetPlaces(area); //, null, false, false
                cell6Data.AddRange(tempPlaces);
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
                    ImageStats info = new ImageStats(area, 80, 100); //values for Cell8 sized area with Cell11 resolution.
                    if (cellToCheck.Length == 8) //We don't want to do the DoPlacesExist check here, since we'll want empty tiles for empty areas at this l
                    {
                        var places = GetPlaces(area, cell6Data); //These are cloned in GetPlaces, so we aren't intersecting areas twice and breaking drawing. //, false, false, 0
                        var tileData = MapTiles.DrawAreaAtSize(info, places);
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

        public static void CreateStandaloneDB(long relationID = 0, GeoArea bounds = null, bool saveToDB = false, bool saveToFolder = true)
        {
            //TODO: could rename TerrainInfo to TrailInfo, terrainDataSmall to trailData

            string name = "";
            if (bounds != null)
                name = Math.Truncate(bounds.SouthLatitude) + "_" + Math.Truncate(bounds.WestLongitude) + "_" + Math.Truncate(bounds.NorthLatitude) + "_" + Math.Truncate(bounds.EastLongitude) + ".sqlite";

            if (relationID > 0)
                name = relationID.ToString() + ".sqlite";

            if (File.Exists(name))
                File.Delete(name);

            var mainDb = new PraxisContext();
            var sqliteDb = new StandaloneContext(relationID.ToString());
            sqliteDb.ChangeTracker.AutoDetectChangesEnabled = false;
            sqliteDb.Database.EnsureCreated();
            Log.WriteLog("Standalone DB created for relation " + relationID + " at " + DateTime.Now);

            GeoArea buffered;
            if (relationID > 0)
            {
                var fullArea = mainDb.StoredOsmElements.Where(m => m.sourceItemID == relationID && m.sourceItemType == 3).FirstOrDefault();
                if (fullArea == null)
                    return;

                buffered = Converters.GeometryToGeoArea(fullArea.elementGeometry);
                //This should also be able to take a bounding box in addition in the future.
                if (relationID == 350381)
                    buffered = new GeoArea(41.27401, -81.97301, 41.6763, -81.3665); //A smaller box that doesn't cross half the lake.
            }
            else
                buffered = bounds;

            //TODO: set a flag to allow this to pull straight from a PBF file? 
            List<StoredOsmElement> allPlaces = new List<StoredOsmElement>();
            var intersectCheck = Converters.GeoAreaToPolygon(buffered);
            bool pullFromPbf = false; //Set via arg at startup? or setting file?
            if (!pullFromPbf)
                allPlaces = GetPlaces(buffered);
            else
            {
                //need a file to read from.
                //optionally a bounding box on that file.
                //Starting to think i might want to track some generic parameters I refer to later. like -box|s|w|n|e or -point|lat|long or -singleFile|here.osm.pbf
                //allPlaces = PbfFileParser.ProcessSkipDatabase();
            }

            Log.WriteLog("Loaded all intersecting geometry at " + DateTime.Now);

            string minCode = new OpenLocationCode(buffered.SouthLatitude, buffered.WestLongitude).CodeDigits;
            string maxCode = new OpenLocationCode(buffered.NorthLatitude, buffered.EastLongitude).CodeDigits;
            int removableLetters = 0;
            for (int i = 0; i < 10; i++)
            {
                if (minCode[i] == maxCode[i])
                    removableLetters++;
                else
                    i += 10;
            }
            string commonStart = minCode.Substring(0, removableLetters);

            var wikiList = allPlaces.Where(a => a.Tags.Any(t => t.Key == "wikipedia") && a.name != "").Select(a => a.name).Distinct().ToList();
            //Leaving this nearly wide open, since it's not the main driver of DB size.
            var basePlaces = allPlaces.Where(a => a.name != "" || a.GameElementName != "unmatched").ToList(); //.Where(a => a.name != "").ToList();// && (a.IsGameElement || wikiList.Contains(a.name))).ToList();
            var distinctNames = basePlaces.Select(p => p.name).Distinct().ToList();//This distinct might be causing things in multiple pieces to only detect one of them, not all of them?

            var placeInfo = CoreComponents.Standalone.Standalone.GetPlaceInfo(basePlaces);
            //Remove trails later.
            //SHORTCUT: for roads that are a straight-enough line (under 1 Cell10 in width or height)
            //just treat them as being 1 Cell10 in that axis, and skip tracking them by each Cell10 they cover.
            HashSet<long> skipEntries = new HashSet<long>();
            foreach (var pi in placeInfo.Where(p => p.areaType == "road" || p.areaType == "trail"))
            {
                //If a road is nearly a straight line, treat it as though it was 1 cell10 wide, and don't index its coverage per-cell later.
                if (pi.height <= ConstantValues.resolutionCell10 && pi.width >= ConstantValues.resolutionCell10)
                { pi.height = ConstantValues.resolutionCell10; skipEntries.Add(pi.OsmElementId); }
                else if (pi.height >= ConstantValues.resolutionCell10 && pi.width <= ConstantValues.resolutionCell10)
                { pi.width = ConstantValues.resolutionCell10; skipEntries.Add(pi.OsmElementId); }    
            }

            sqliteDb.PlaceInfo2s.AddRange(placeInfo);
            sqliteDb.SaveChanges();
            Log.WriteLog("Processed geometry at " + DateTime.Now);
            var placeDictionary = placeInfo.ToDictionary(k => k.OsmElementId, v => v);

            //to save time, i need to index which areas are in which Cell6.
            //So i know which entries I can skip when running.
            var indexCell6 = CoreComponents.Standalone.Standalone.IndexAreasPerCell6(buffered, basePlaces);
            var indexes = indexCell6.SelectMany(i => i.Value.Select(v => new PlaceIndex() { PlusCode = i.Key, placeInfoId = placeDictionary[v.sourceItemID].id})).ToList();
            sqliteDb.PlaceIndexs.AddRange(indexes);
            
            sqliteDb.SaveChanges();
            Log.WriteLog("Processed Cell6 index table at " + DateTime.Now);            

            //trails need processed the old way, per Cell10, when they're not simply a straight-line.
            //Roads too.
            var tdSmalls = new Dictionary<string, TerrainDataSmall>(); //Possible issue: a trail and a road with the same name would only show up as whichever one got in the DB first.
            var toRemove = new List<PlaceInfo2>();
            foreach (var trail in basePlaces.Where(p => (p.GameElementName == "trail" || p.GameElementName == "road"))) //TODO: add rivers here?
            {
                if (skipEntries.Contains(trail.sourceItemID))
                    continue; //Don't per-cell index this one, we shifted it's envelope to handle it instead.

                if (trail.name == "")
                    continue; //So sorry, but there's too damn many roads without names inflating DB size without being useful as-is.

                //var pis = placeInfo.Where(p => p.OsmElementId == trail.sourceItemID).ToList();
                var p = placeDictionary[trail.sourceItemID];
                toRemove.Add(p);

                //I should search the element for the cell10s it overlaps, not the Cell8s for cells with the elements.
                GeoArea thisPath = Converters.GeometryToGeoArea(trail.elementGeometry);
                List<StoredOsmElement> oneEntry = new List<StoredOsmElement>();
                oneEntry.Add(trail);

                var overlapped = AreaTypeInfo.SearchArea(ref thisPath, ref oneEntry, true);
                foreach (var o in overlapped)
                {
                    foreach (var oo in o.Value)
                    {
                        tdSmalls.TryAdd(oo.Name, new TerrainDataSmall() { Name = oo.Name, areaType = oo.areaType });
                    }
                    var ti = new TerrainInfo();
                    ti.PlusCode = o.Key.Substring(removableLetters, 10 - removableLetters);
                    ti.TerrainDataSmall = o.Value.Select(oo => tdSmalls[oo.Name]).ToList();
                    sqliteDb.TerrainInfo.Add(ti);
                }
                sqliteDb.SaveChanges();
            }
            
            foreach (var r in toRemove.Distinct())
                sqliteDb.PlaceInfo2s.Remove(r);
            sqliteDb.SaveChanges();
            Log.WriteLog("Trails processed at " + DateTime.Now);

            //make scavenger hunts
            var sh = CoreComponents.Standalone.Standalone.GetScavengerHunts(allPlaces);
            sqliteDb.ScavengerHunts.AddRange(sh);
            sqliteDb.SaveChanges();
            Log.WriteLog("Auto-created scavenger hunt entries at " + DateTime.Now);

            var swCorner = new OpenLocationCode(intersectCheck.EnvelopeInternal.MinY, intersectCheck.EnvelopeInternal.MinX);
            var neCorner = new OpenLocationCode(intersectCheck.EnvelopeInternal.MaxY, intersectCheck.EnvelopeInternal.MaxX);
            //insert default entries for a new player.
            sqliteDb.PlayerStats.Add(new PlayerStats() { timePlayed = 0, distanceWalked = 0, score = 0 });
            sqliteDb.Bounds.Add(new Bounds() { EastBound = neCorner.Decode().EastLongitude, NorthBound = neCorner.Decode().NorthLatitude, SouthBound = swCorner.Decode().SouthLatitude, WestBound = swCorner.Decode().WestLongitude, commonCodeLetters = commonStart });
            sqliteDb.SaveChanges();

            //now we have the list of places we need to be concerned with. 
            System.IO.Directory.CreateDirectory(relationID + "Tiles");
            CoreComponents.Standalone.Standalone.DrawMapTilesStandalone(relationID, buffered, allPlaces, saveToFolder);
            sqliteDb.SaveChanges();
            Log.WriteLog("Maptiles drawn at " + DateTime.Now);

            //Copy the files as necessary to their correct location.
            if (saveToFolder)
                Directory.Move(relationID + "Tiles", ParserSettings.Solar2dExportFolder + "Tiles");

            File.Copy(relationID + ".sqlite", ParserSettings.Solar2dExportFolder + "database.sqlite");

            Log.WriteLog("Standalone gameplay DB done.");
        }

        public static void SingleTest()
        {
            //trying to find one relation to fix.
            string filename = ParserSettings.PbfFolder + "ohio-latest.osm.pbf";
            long oneId = 6113131;

            FileStream fs = new FileStream(filename, FileMode.Open);
            var source = new PBFOsmStreamSource(fs);
            var relation = source.ToComplete().Where(s => s.Type == OsmGeoType.Relation && s.Id == oneId).Select(s => (OsmSharp.Complete.CompleteRelation)s).FirstOrDefault();
            var converted = GeometrySupport.ConvertOsmEntryToStoredElement(relation);
            StoredOsmElement sw = new StoredOsmElement();
            Log.WriteLog("Relevant data pulled from file and converted at" + DateTime.Now);

            string destFileName = System.IO.Path.GetFileNameWithoutExtension(filename);
            GeometrySupport.WriteSingleStoredElementToFile(ParserSettings.JsonMapDataFolder + destFileName + "-MapData-Test.json", converted);
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
    }
}
