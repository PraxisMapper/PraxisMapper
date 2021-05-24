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

            if (args.Any(a => a == "-loadPbfsToDb"))
            {
                List<string> filenames = System.IO.Directory.EnumerateFiles(ParserSettings.PbfFolder, "*.pbf").ToList();
                foreach (string filename in filenames)
                {
                    var fs = File.OpenRead(filename);
                    var osmStream = new PBFOsmStreamSource(fs);
                    PbfFileParser.ProcessFileCoreV4(osmStream);
                }
            }

            if (args.Any(a => a == "-loadPbfsToJson"))
            {
                List<string> filenames = System.IO.Directory.EnumerateFiles(ParserSettings.PbfFolder, "*.pbf").ToList();
                foreach (string filename in filenames)
                {
                    string jsonFileName = ParserSettings.JsonMapDataFolder + Path.GetFileNameWithoutExtension(filename) + ".json";
                    var fs = File.OpenRead(filename);
                    var osmStream = new PBFOsmStreamSource(fs);
                    PbfFileParser.ProcessFileCoreV4(osmStream, true, jsonFileName);
                }
            }

            if (args.Any(a => a == "-loadJsonToDb"))
            {
                List<string> filenames = System.IO.Directory.EnumerateFiles(ParserSettings.JsonMapDataFolder, "*.json").ToList();
                foreach (var jsonFileName in filenames)
                {
                    var entries = GeometrySupport.ReadStoredElementsFileToMemory(jsonFileName);
                    var db = new PraxisContext();
                    db.StoredOsmElements.AddRange(entries);
                    db.SaveChanges();
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

            if (args.Any(a => a.StartsWith("-createStandalone")))
            {
                //This makes a standalone DB for a specific relation passed in as a paramter. Originally used a geoArea, now calculates that from a Relation.
                //If you think you want an area, it's probably better to pick an admin boundary as a relation that covers the area.

                int relationId = args.Where(a => a.StartsWith("-createStandalone")).First().Split('|')[1].ToInt();
                CreateStandaloneDB(relationId, false, true); //How map tiles are handled is determined by the optional parameters
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

            //if (args.Any(a => a.StartsWith("-importV4")))
            //{
                // 4th generation of logic for importing OSM data from PBF file.
                //V4 rules:
                //Tags will be saved to a separate table. (this lets me update formatting rules without reimporting data).
                //All entries  are processed into the database as geometry objects into one table.
                //Use the built-in Feature converter in OsmSharp.Geo instead of maintaining the OSM-to-Geometry logic myself.
                //Only nodes that match the tag rules will be imported as their own entries. Untagged nodes will be ignored.
                //attempt to stream data to avoid memory issues. MIght need to do multiple filters on small areas (degree square? half-degree?)

                //I need to avoid adding duplicate entries, and i dont want to do huge passes per entry added. 
                //Is there an 'ignore on duplicate' command or setting in MariaDB and SQL Server? I might need to set those. CAn it be the default behavior?

                //NOTE: this worked fine once. I added node processing, and then it crashed on an OOM error at 8GB? But i also had my usual system stuff running in the background, include a browser.
                //Continue testing on bigger files to see if there's issues somewhere still. If so, might need to implement in some skip/take logic to stay within ram limit
            //    TagParser.Initialize(true);
            //    List<string> filenames = System.IO.Directory.EnumerateFiles(ParserSettings.PbfFolder, "*.pbf").ToList();
            //    foreach (string filename in filenames)
            //    {
            //        V4Import.ProcessFilePiecesV4(filename);
            //    }
            //}

            //new V4 options to piecemeal up some of the process.
            if (args.Any(a => a.StartsWith("-splitToSubPbfs")))
            {
                //This should generally be done on large files to make sure each sub-file is complete. It won't merge results if you run it on 2 overlapping
                //extract files.
                Log.WriteLog("Loading large file to split now. Remember to use only the largest extract file you have for this or results will not be as expected.");

                var filename = System.IO.Directory.EnumerateFiles(ParserSettings.PbfFolder, "*.pbf").Where(f => !f.StartsWith("split")).First(); //don't look at any existing split files.
                Log.WriteLog("Loading " + filename + " to split. Remember to use only the largest extract file you have for this or results will not be as expected.");
                V4Import.SplitPbfToSubfiles(filename);
            }

            //testing generic image drawing function
            //if (args.Any(a => a.StartsWith("-testDrawOhio")))
            //{
            //    //remove admin boundaries from the map.
            //    TagParser.Initialize(true);
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
        public static void CreateStandaloneDB(long relationID, bool saveToDB = false, bool saveToFolder = true)
        {
            //TODO: add a parmeter to determine if maptiles are saved in the DB or separately in a folder.            
            //TODO in practice:
            //Set up area name to be area type  if name is blank.
            //

            var mainDb = new PraxisContext();
            var sqliteDb = new StandaloneContext(relationID.ToString());// "placeholder";
            sqliteDb.Database.EnsureCreated();

            var fullArea = mainDb.StoredOsmElements.Where(m => m.sourceItemID == relationID && m.sourceItemType == 3).FirstOrDefault();
            if (fullArea == null)
                return;

            //Add a Cell8's worth of space to the edges of the area.
            GeoArea buffered = new GeoArea(fullArea.elementGeometry.EnvelopeInternal.MinY, fullArea.elementGeometry.EnvelopeInternal.MinX, fullArea.elementGeometry.EnvelopeInternal.MaxY, fullArea.elementGeometry.EnvelopeInternal.MaxX);
            //var intersectCheck = Converters.GeoAreaToPreparedPolygon(buffered);
            var intersectCheck = Converters.GeoAreaToPolygon(buffered); //Cant use prepared geometry against the db directly

            var allPlaces = mainDb.StoredOsmElements.Where(md => intersectCheck.Intersects(md.elementGeometry)).ToList();

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
                    var areaList = allPlaces.Where(a => a.elementGeometry.Intersects(acheck2)).Select(a => a.Clone()).ToList();

                    System.Text.StringBuilder terrainInfo = AreaTypeInfo.SearchArea(ref areaForTile, ref areaList, true);
                    var splitData = terrainInfo.ToString().Split(Environment.NewLine);
                    foreach (var sd in splitData)
                    {
                        if (sd == "") //last result is always a blank line
                            continue;

                        var subParts = sd.Split('|'); //PlusCode|Name|AreaTypeID|MapDataID
                        sqliteDb.TerrainInfo.Add(new TerrainInfo() { Name = subParts[1], areaType = subParts[2].ToInt(), PlusCode = subParts[0], MapDataID = subParts[3].ToInt() });
                    }

                    //var tile = MapTiles.DrawAreaMapTileSkia(ref areaList, areaForTile, 11);
                    var info = new ImageStats(areaForTile, 80, 100); //These are Cell11-sized. I no longer need to specifically call that out with a specific function.
                    var tile = MapTiles.DrawAreaAtSizeV4(info, areaList);

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
