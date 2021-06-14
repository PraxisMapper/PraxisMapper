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
using Microsoft.EntityFrameworkCore;
using static CoreComponents.StandaloneDbTables;
using System.Collections.Concurrent;

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

            //This is now done just by not having a TagParser entry for these items.
            //if (args.Any(a => a == "-skipRoadsAndBuildings"))
            //{
            //    //DbSettings.processRoads = false;
            //    //DbSettings.processBuildings = false;
            //    //DbSettings.processParking = false;
            //}

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
                    CoreComponents.PbfReader r = new PbfReader();
                    r.outputPath = ParserSettings.JsonMapDataFolder;
                    r.ProcessFile(filename);
                    File.Move(filename, filename + "done");
                    Log.WriteLog("Finished loaded " + filename + " to JSON at " + DateTime.Now);
                }
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
                        //TODO: split off Tasks to convert these so the StreamReader doesn't have to wait on the converter/DB for progress
                        //SR only hits .9MB/s for this task, and these are multigigabyte files
                        //But watch out for multithreading gotchas like usual.
                        //Would be: string = sr.Readline(); task -> convertStoredElement(string); lock and add to shared collection; after 100,000 entries lock then add collection to db and save changes.
                        //await all tasks once end of stream is hit. lock and add last elements to DB
                        StoredOsmElement stored = GeometrySupport.ConvertSingleJsonStoredElement(sr.ReadLine());
                        db.StoredOsmElements.Add(stored);
                        entryCounter++;
                        
                        if (entryCounter > 100000)
                        {
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
                        var nextTask = Task.Run(() => {
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
                    while(loopAmount * loopCount < total)
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
                    Log.WriteLog("Final entries for "+ jsonFileName + " completed at " + DateTime.Now);

                    
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
                //This will be testing with Cuyahoga County, id 350381. Note that it technically extends to the Canadian border halfway across Lake Erie, so it'll have a lot of empty blue map tiles.
                //It uses about 100MB of maptiles, takes ~20 minutes to process maptiles for.
                //or use id 6113131 for CWRU, a much smaller location than a county.
                //A typical county might be a little too big for the Solar2D framework i set up.

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

            //if (args.Any(a => a == "-extractBigAreas"))
            //{
                //PbfOperations.ExtractAreasFromLargeFile(ParserSettings.PbfFolder + "planet-latest.osm.pbf"); //Guarenteed to have everything. Estimates 400+ minutes per run, including loading country boundaries
                //PbfOperations.ExtractAreasFromLargeFile(ParserSettings.PbfFolder + "north-america-latest.osm.pbf");
            //}

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
                var fakeplaces = db.GeneratedMapData.Where(md => md.place.Intersects(location6)).ToList();

                for (int x = 0; x < 20; x++)
                {
                    for (int y = 0; y < 20; y++)
                    {
                        string cell8 = cell6 + OpenLocationCode.CodeAlphabet[x] + OpenLocationCode.CodeAlphabet[y];
                        CodeArea box = OpenLocationCode.DecodeValid(cell8);
                        var location = Converters.GeoAreaToPolygon(box);
                        if (!places.Any(md => md.elementGeometry.Intersects(location)) && !fakeplaces.Any(md => md.place.Intersects(location)))
                            CreateInterestingPlaces(cell8);
                    }
                }
            }

            //new V4 options to piecemeal up some of the process.
            //if (args.Any(a => a.StartsWith("-splitToSubPbfs")))
            //{
            //    //This should generally be done on large files to make sure each sub-file is complete. It won't merge results if you run it on 2 overlapping
            //    //extract files.
            //    Log.WriteLog("Loading large file to split now. Remember to use only the largest extract file you have for this or results will not be as expected.");

            //    var filename = System.IO.Directory.EnumerateFiles(ParserSettings.PbfFolder, "*.pbf").Where(f => !f.StartsWith("split")).First(); //don't look at any existing split files.
            //    Log.WriteLog("Loading " + filename + " to split. Remember to use only the largest extract file you have for this or results will not be as expected.");
            //    V4Import.SplitPbfToSubfiles(filename);
            //}

            //testing generic image drawing function
            //if (args.Any(a => a.StartsWith("-testDrawOhio")))
            //{
            //    //remove admin boundaries from the map.
            //    var makeAdminClear = TagParser.styles.Where(s => s.name == "admin").FirstOrDefault();
            //    makeAdminClear.paint.Color = SKColors.Transparent;

            //    //GeoArea ohio = new GeoArea(38, -84, 42, -80); //All of the state, hits over 10GB in C# memory space
            //    //GeoArea ohio = new GeoArea(41.2910, -81.9257, 41.6414,  -81.3970); //Cleveland
            //    GeoArea ohio = new GeoArea(41.49943, -81.61139, 41.50612, -81.60201); //CWRU
            //    var ohioPoly = Converters.GeoAreaToPolygon(ohio);
            //    var db = new PraxisContext();
            //    db.Database.SetCommandTimeout(900);
            //    var stuffToDraw = db.StoredOsmElements.Include(c => c.Tags).Where(s => s.elementGeometry.Intersects(ohioPoly)).OrderByDescending(w => w.elementGeometry.Area).ThenByDescending(w => w.elementGeometry.Length).ToList();

            //    //NOTE ordering is skipped to get data back in a reasonable time. This is testing my draw logic speed, not MariaDB's sort speed.
            //    //TODO: add timestamps for how long these take to draw. Will see how fast Skia is at various scales. Is Skia faster on big images? no, thats a fluke.
            //    File.WriteAllBytes("cwru-512.png", MapTiles.DrawAreaAtSizeV4(ohio, 512, 512, stuffToDraw)); //63 ohio, 3.3 cleveland
            //    File.WriteAllBytes("cwru-1024.png", MapTiles.DrawAreaAtSizeV4(ohio, 1024, 1024, stuffToDraw));//63.7 seconds, 3.6 cleveland
            //    File.WriteAllBytes("cleveland-2048.png", MapTiles.DrawAreaAtSizeV4(ohio, 2048, 2048, stuffToDraw)); //58 seconds, 3.9 cleveland
            //    File.WriteAllBytes("cleveland-4096.png", MapTiles.DrawAreaAtSizeV4(ohio, 4096, 4096, stuffToDraw)); //51 seconds, 5.3 cleveland
            //    File.WriteAllBytes("cleveland-reallybig.png", MapTiles.DrawAreaAtSizeV4(ohio, 15000, 15000, stuffToDraw)); //100 seconds, 21.7 cleveland
            //}

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
                        var tileData = MapTiles.DrawAreaAtSizeV4(info, places); //MapTiles.DrawAreaMapTileSkia(ref places, area, 11); //now Skia drawing, should be faster. Peaks around 2600/s. ImageSharp peaked at 1600/s.
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
        public static void CreateStandaloneDB(long relationID = 0, GeoArea bounds = null, bool saveToDB = false, bool saveToFolder = true)
        {
            //Time to update this logic
            //Step 1: make maptiles and store them as requested (in DB or in a folder)
            //Step 2: calculate areas types for gameplay modes that will use that (incremental game)
            //Step 3: Create scavenger hunt list(s) automatically off the following traits
            //       a: Wikipedia linked entries
            //       b: IsGameElement matching tags.
            //NOTE 1: If both relation and bounds are provided, name it after the relation but use the bounds for processing logic.

            //So, Solar2D only opens 1 DB at once, so I will want to minimize storage space here.
            //New table: terrainData. Holds name/type for TerrainInfo. TerrainInfo now holds a Foreign Key to TerrainData
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
            //Would need a variant of ProcessFileCore that returns a list of StoredOsmElements intead of writing directly to DB
            List<StoredOsmElement> allPlaces = new List<StoredOsmElement>();
            var intersectCheck = Converters.GeoAreaToPolygon(buffered);
            bool pullFromPbf = false; //Set via arg at startup? or setting file?
            if (!pullFromPbf)
                allPlaces = mainDb.StoredOsmElements.Include(m => m.Tags).Where(md => intersectCheck.Intersects(md.elementGeometry)).ToList();
            else
            {
                //need a file to read from.
                //optionally a bounding box on that file.
                //Starting to think i might want to track some generic parameters I refer to later. like -box|s|w|n|e or -point|lat|long or -singleFile|here.osm.pbf
                //allPlaces = PbfFileParser.ProcessSkipDatabase();
            }

            foreach (var a in allPlaces)
                TagParser.GetStyleForOsmWay(a); //fill in IsGameElement and gameElementName once instead of doing that lookup every Cell10.

            Log.WriteLog("Loaded all intersecting geometry at " + DateTime.Now);

            //now we have the list of places we need to be concerned with. 
            //start drawing maptiles and sorting out data.
            var swCorner = new OpenLocationCode(intersectCheck.EnvelopeInternal.MinY, intersectCheck.EnvelopeInternal.MinX);
            var neCorner = new OpenLocationCode(intersectCheck.EnvelopeInternal.MaxY, intersectCheck.EnvelopeInternal.MaxX);

            //declare how many map tiles will be drawn
            var xTiles = buffered.LongitudeWidth / resolutionCell8;
            var yTiles = buffered.LatitudeHeight / resolutionCell8;
            var totalTiles = Math.Truncate(xTiles * yTiles);

            Log.WriteLog("Starting processing Cell8 terrain and tiles for " + totalTiles + " Cell8 areas.");
            long mapTileCounter = 0;
            System.Diagnostics.Stopwatch progressTimer = new System.Diagnostics.Stopwatch();
            progressTimer.Start();
            System.IO.Directory.CreateDirectory(relationID + "Tiles");
            //now, for every Cell8 involved, draw and name it.
            //This is tricky to run in parallel because it's not smooth increments
            var yCoords = new List<double>();
            var yVal = swCorner.Decode().SouthLatitude;
            while (yVal <= neCorner.Decode().NorthLatitude)
            {
                yCoords.Add(yVal);
                yVal += resolutionCell8;
            }

            var xCoords = new List<double>();
            var xVal = swCorner.Decode().WestLongitude;
            while (xVal <= neCorner.Decode().EastLongitude)
            {
                xCoords.Add(xVal);
                xVal += resolutionCell8;
            }

            System.Threading.ReaderWriterLockSlim dbLock = new System.Threading.ReaderWriterLockSlim();

            ConcurrentDictionary<string, TerrainDataSmall> terrainDatas = new ConcurrentDictionary<string, TerrainDataSmall>();

            //fill in wiki list early, so we can apply it to each tile.
            var wikiList = allPlaces.Where(a => a.Tags.Any(t => t.Key == "wikipedia")).Select(a => a.name).Distinct().ToList();
            //TODO: might include cities for county-sized games. But then I have to track a city for each Cell10 in the map.
            var cityList = allPlaces.Where(a => a.Tags.Any(t => t.Key == "admin_level" && t.Value == "8") && a.Tags.Any(t => t.Key == "boundary" && t.Value == "administrative")).Select(a => a.name).Distinct().ToList();

            //TODO: concurrent collections might require a lock or changing types.
            //But running in parallel saves a lot of time in general on this.
            Parallel.ForEach(yCoords, y =>
            {
                Parallel.ForEach(xCoords, x =>
                {
                    //make map tile.
                    var plusCode = new OpenLocationCode(y, x, 10);
                    var plusCode8 = plusCode.CodeDigits.Substring(0, 8);
                    var plusCodeArea = OpenLocationCode.DecodeValid(plusCode8);

                    var areaForTile = new GeoArea(new GeoPoint(plusCodeArea.SouthLatitude, plusCodeArea.WestLongitude), new GeoPoint(plusCodeArea.NorthLatitude, plusCodeArea.EastLongitude));
                    var acheck = Converters.GeoAreaToPolygon(areaForTile); //this is faster than using a PreparedPolygon in testing, which was unexpected.
                    var areaList = allPlaces.Where(a => acheck.Intersects(a.elementGeometry)).ToList(); //This one is for the maptile
                    var gameAreas = areaList.Where(a => a.IsGameElement).ToList(); //this is for determining what area name/type to display for a cell10. //This might always be null. PIck up here on fixing standalone mode.
                    //NOTE: gameAreas might also need to include ScavengerHunt entries to ensure those process correcly.

                    //Create the maptile first, so if we save it to the DB/a file we can call the lock once per loop.
                    var info = new ImageStats(areaForTile, 80, 100); //Each pixel is a Cell11, we're drawing a Cell8.
                    var tile = MapTiles.DrawAreaAtSizeV4(info, areaList);
                    if (tile == null)
                    {
                        Log.WriteLog("Tile at " + x + "," + y + "Failed to draw!");
                        return;
                    }
                    if (saveToFolder) //some apps, like my Solar2D apps, can't use the byte[] in a DB row and need files.
                    {
                        //This split helps (but does not alleviate) Solar2D performance.
                        //A county-sized app will function this way, though sometimes when zoomed out it will not load all map tiles on an android device.
                        Directory.CreateDirectory(relationID + "Tiles\\" + plusCode8.Substring(0, 6));
                        System.IO.File.WriteAllBytes(relationID + "Tiles\\" + plusCode8.Substring(0, 6) + "\\" + plusCode8.Substring(6, 2) + ".pngTile", tile); //Solar2d also can't load pngs directly from an apk file in android, but the rule is extension based.
                    }

                    //SearchArea didn't need any logic changes, but we do want to pass in only game areas.
                    //TODO: this might not be correctly returning multiple entries, or I might not be saving them to the SQLite DB. Investigate.
                    var terrainInfo = AreaTypeInfo.SearchArea2(ref areaForTile, ref gameAreas, true); //The list of Cell10 areas in this Cell8. Can be null if a whole area has 0 IsGameElement entries
                    //var splitData = terrainInfo.ToString().Split(Environment.NewLine);
                    if (terrainInfo != null)
                    {
                        dbLock.EnterWriteLock();
                        foreach (var ti in terrainInfo)
                        {
                            TerrainInfo tiInsert = new TerrainInfo() { PlusCode = ti.Key, TerrainData = new List<TerrainDataSmall>() };
                        foreach (var td in ti.Value)
                        {
                                string key = td.Name + "|" + td.areaType;
                            if (!terrainDatas.ContainsKey(key))
                                    terrainDatas.TryAdd(key, new TerrainDataSmall() { Name = td.Name, areaType = td.areaType});

                                tiInsert.TerrainData.Add(terrainDatas[key]);
                            }
                            sqliteDb.TerrainInfo.Add(tiInsert);
                        }
                        dbLock.ExitWriteLock();
                    }

                    mapTileCounter++;
                    if (progressTimer.ElapsedMilliseconds > 15000)
                    {
                        Log.WriteLog(mapTileCounter + " cells processed, " + Math.Round((mapTileCounter / totalTiles) * 100, 2) + "% complete");
                        progressTimer.Restart();
                    }
                });
            });
            Log.WriteLog(mapTileCounter + " maptiles drawn at " + DateTime.Now);

            foreach (var td in terrainDatas)
                sqliteDb.TerrainDataSmall.Add(td.Value);

            sqliteDb.SaveChanges(); //inserts all the TerrainInfo elements now, and maptiles if they're injected into the Sqlite file.
            Log.WriteLog("Terrain Data and Info saved at " + DateTime.Now);

            //Create automatic scavenger hunt entries.
            Dictionary<string, List<string>> scavengerHunts = new Dictionary<string, List<string>>();

            
            //NOTE:
            //If i run this by elementID, i get everything unique but several entries get duplicated becaues they're in multiple pieces.
            //If I run this by name, the lists are much shorter but visiting one distinct location might count for all of them (This is a bigger concern with very large areas or retail establishment)
            //So I'm going to run this by name for the player's sake. 
            scavengerHunts.Add("Wikipedia Places", wikiList);
            Log.WriteLog(wikiList.Count() + " Wikipedia-linked items found for scavenger hunt.");
            //fill in gameElement lists.
            foreach (var gameElementTags in TagParser.styles.Where(s => s.IsGameElement))
            {
                var foundElements = allPlaces.Where(a => TagParser.MatchOnTags(gameElementTags, a) && !string.IsNullOrWhiteSpace(a.name)).Select(a => a.name).Distinct().ToList();
                scavengerHunts.Add(gameElementTags.name, foundElements);
                Log.WriteLog(foundElements.Count() + " " + gameElementTags.name + " items found for scavenger hunt.");
            }

            foreach (var hunt in scavengerHunts)
            {
                foreach (var item in hunt.Value)
                    sqliteDb.ScavengerHunts.Add(new ScavengerHuntStandalone() { listName = hunt.Key, description = item, playerHasVisited = false });
            }
            Log.WriteLog("Auto-created scavenger hunt entries at " + DateTime.Now);
            sqliteDb.SaveChanges();

            //insert default entries for a new player.
            sqliteDb.PlayerStats.Add(new PlayerStats() { timePlayed = 0, distanceWalked = 0, score = 0 });
            sqliteDb.Bounds.Add(new Bounds() { EastBound = neCorner.Decode().EastLongitude, NorthBound = neCorner.Decode().NorthLatitude, SouthBound = swCorner.Decode().SouthLatitude, WestBound = swCorner.Decode().WestLongitude });
            sqliteDb.SaveChanges();

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
            //DBCommands.AddMapDataToDBFromFiles();
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
