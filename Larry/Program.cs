using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using OsmSharp.Complete;
using PraxisCore;
using PraxisCore.PbfReader;
using PraxisCore.Support;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using static PraxisCore.ConstantValues;
using static PraxisCore.DbTables;
using static PraxisCore.Place;
using static PraxisCore.Singletons;

namespace Larry
{
    class Program
    {
        static IConfigurationRoot config;
        static List<DbTables.Place> memorySource;
        static IMapTiles MapTiles;
        static bool singleThread = false;

        static void Main(string[] args)
        {
            var builder = new ConfigurationBuilder()
            .AddJsonFile("Larry.config.json");
            config = builder.Build();

            ApplyConfigValues();
            //If multiple args are supplied, run them in the order that make sense, not the order the args are supplied.
            if (args.Any(a => a == "-createDB")) //setup the destination database
            {
                createDb();
            }

            Log.WriteLog("Larry started at " + DateTime.Now);
            if (args.Length == 0)
            {
                Log.WriteLog("You must pass an arguement to this application", Log.VerbosityLevels.High);
                //TODO: list valid commands or point at the docs file
                return;
            }

            string dbName;
            if (config["DbMode"] != "LocalDB")
            {
                dbName = config["DbConnectionString"].Split(";").First(s => s.StartsWith("database"));
                Log.WriteLog("Using connection for " + dbName);
            }

            //if (args.Any(a => a.StartsWith("-getPbf:")))
            //{
            //    //Wants 3 pieces. Drops in placeholders if some are missing. Giving no parameters downloads Ohio.
            //    string arg = args.First(a => a.StartsWith("-getPbf:")).Replace("-getPbf:", "");
            //    var splitData = arg.Split('|'); //remember the first one will be empty.
            //    string level1 = splitData.Count() >= 4 ? splitData[3] : "north-america";
            //    string level2 = splitData.Count() >= 3 ? splitData[2] : "us";
            //    string level3 = splitData.Count() >= 2 ? splitData[1] : "ohio";

            //    DownloadPbfFile(level1, level2, level3, config["PbfFolder"]);
            //}

            if (args.Any(a => a == "-resetFiles"))
            {
                ResetFiles(config["PbfFolder"]);
            }

            if (args.Any(a => a == "-rollDefaultPasswords"))
            {
                SetDefaultPasswords();
            }

            if (!args.Any(a => a == "-makeServerDb")) //This will not be available until after creating the DB slightly later.
                TagParser.Initialize(config["ForceStyleDefaults"] == "True", MapTiles); //This last bit of config must be done after DB creation check


            if (args.Any(a => a == "-loadData"))
            {
                LoadEverything();
            }

            //This is the single command to get a server going, assuming you have done all the setup steps yourself beforehand and your config is correct. 
            //NOTE: the simplest setup possible is to grab map data and run PraxisMapper.exe directly now, and this is the 2nd best choice now.
            if (args.Any(a => a == "-makeServerDb"))
            {
                Stopwatch sw = new Stopwatch();
                sw.Start();
                SetEnvValues();
                SetDefaultPasswords();
                var db = new PraxisContext();
                TagParser.Initialize(true, null);
                createDb();
                LoadEverything();
                db.SetServerBounds(long.Parse(config["UseOneRelationID"]));
                Log.WriteLog("Server setup complete in " + sw.Elapsed);
            }

            if (args.Any(a => a == "-resetStyles"))
            {
                using var db = new PraxisContext();
                db.ResetStyles();
            }

            TagParser.Initialize(config["ForceStyleDefaults"] == "True", MapTiles);


            //Takes a styleSet, and saves 1 output file per style in that set. 
            if (args.Any(a => a.StartsWith("-splitPbfByStyle:")))
            {
                var style = args.First(a => a.StartsWith("-splitPbfByStyle:")).Split(':')[1];
                List<string> filenames = System.IO.Directory.EnumerateFiles(config["PbfFolder"], "*.pbf").ToList();
                foreach (string filename in filenames)
                {
                    Log.WriteLog("Loading " + filename + " at " + DateTime.Now);
                    PbfReader r = new PbfReader();
                    r.outputPath = config["PbfFolder"];
                    r.styleSet = style;
                    r.processingMode = config["processingMode"]; // "normal" and "center" allowed
                    r.saveToDB = false; //we want these as separate files for later.
                    //r.onlyMatchedAreas = config["OnlyTaggedAreas"] == "True";
                    //r.reprocessFile = config["reprocessFiles"] == "True";
                    r.splitByStyleSet = true; //Implies saving to PMD files.

                    if (config["ResourceUse"] == "low")
                    {
                        r.lowResourceMode = true;
                    }
                    else if (config["ResourceUse"] == "high")
                    {
                        r.keepAllBlocksInRam = true; //Faster performance, but files use vastly more RAM than they do HD space. 200MB file = ~6GB total RAM last I checked.
                    }
                    r.ProcessFile(filename, long.Parse(config["UseOneRelationID"]));
                    File.Move(filename, filename + "done");
                }
            }

            if (args.Any(a => a.StartsWith("-convertPbfs")))
            {
                ConvertPBFtoPMD();
            }

            if (args.Any(a => a.StartsWith("-addOneElement:")))
            {
                var vals = args.First(a => a.StartsWith("-addOneElement:")).Split(':');
                LoadOneEntryFromFile(vals[1].ToLong());
            }

            if (args.Any(a => a.StartsWith("-createStandaloneRelation")))
            {
                //This makes a standalone DB for a specific relation passed in as a paramter. 
                int relationId = Int32.Parse(config["UseOneRelationID"]);
                StandaloneCreation.CreateStandaloneDB(relationId, null, false, true); //How map tiles are handled is determined by the optional parameters
            }

            if (args.Any(a => a.StartsWith("-createStandaloneBox")))
            {
                //This makes a standalone DB for a specific area passed in as a paramter.
                //If you want to cover a region in a less-specific way, or the best available relation is much larger than you thought, this might be better.
                string[] bounds = args.First(a => a.StartsWith("-createStandaloneBox")).Split('|');
                GeoArea boundsArea = new GeoArea(bounds[1].ToDouble(), bounds[2].ToDouble(), bounds[3].ToDouble(), bounds[4].ToDouble());

                //in order, these go south/west/north/east.
                StandaloneCreation.CreateStandaloneDB(0, boundsArea, false, true); //How map tiles are handled is determined by the optional parameters
            }

            if (args.Any(a => a.StartsWith("-createStandalonePoint")))
            {
                //This makes a standalone DB centered on a specific point, it will grab a Cell6's area around that point.
                string[] bounds = args.First(a => a.StartsWith("-createStandalonePoint")).Split('|');

                var resSplit = resolutionCell6 / 2;
                GeoArea boundsArea = new GeoArea(bounds[1].ToDouble() - resSplit, bounds[2].ToDouble() - resSplit, bounds[1].ToDouble() + resSplit, bounds[2].ToDouble() + resSplit);

                //in order, these go south/west/north/east.
                StandaloneCreation.CreateStandaloneDB(0, boundsArea, false, true); //How map tiles are handled is determined by the optional parameters
            }

            if (args.Any(a => a == "-autoCreateMapTiles"))
            {
                using var db = new PraxisContext();
                var bounds = db.SetServerBounds(long.Parse(config["UseOneRelationID"]));
                MapTileSupport.PregenMapTilesForArea(bounds);
            }

            if (args.Any(a => a == "-autoCreateSlippyMapTiles"))
            {
                using var db = new PraxisContext();
                var bounds = db.SetServerBounds(long.Parse(config["UseOneRelationID"]));
                for (int zoom = 2; zoom <= 18; zoom++)
                    MapTileSupport.PregenSlippyMapTilesForArea(bounds, zoom);
            }

            if (args.Any(a => a == "-findServerBounds"))
            {
                using var db = new PraxisContext();
                db.SetServerBounds(long.Parse(config["UseOneRelationID"]));
            }

            if (args.Any(a => a.StartsWith("-drawOneImage:")))
            {
                DrawOneImage(args.First(a => a.StartsWith("-drawOneImage:")).Split(":")[1]);
            }

            //TODO: this should 
            if (args.Any(a => a.StartsWith("-processCoastlines:")))
            {
                //NOTE: this is intended to read through the water polygon file. It'll probably run with the coastline linestring file, but that 
                //isn't going to draw what you want in PraxisMapper.
                string arg = args.First(a => a.StartsWith("-processCoastlines:"));
                string filename = arg.Substring(arg.IndexOf(':') + 1);
                ReadCoastlineWaterPolyShapefile(filename);
            }

            if (args.Any(a => a == "-makePosterImage"))
            {
                DrawPosterOfServer();
            }

            if (args.Any(a => a == "-pwdSpeedTest"))
            {
                PwdSpeedTest();
            }

            if (args.Any(a => a == "-setEnvValues"))
            {
                SetEnvValues();
            }

            if (args.Any(a => a == "-makeOfflineFiles"))
            {
                //TODO: create lastofflineentry file here, remove check for file existin inside makeOfflineJson
                //MakeOfflineFilesCell8();
                MakeOfflineJson("");
                File.Delete("lastOfflineEntry.txt");
            }

            if (args.Any(a => a == "-recalcDrawHints"))
            {
                RecalcDrawSizeHints();
            }

            if (args.Any(a => a.StartsWith("-extractSubMap:")))
            {
                var pieces = args.First(a => a.StartsWith("-extractSubMap:")).Split(":");
                long placeId = Int64.Parse(pieces[1]);
                long placeTypeId = Int64.Parse(pieces[2]);
                ExtractSubMap(placeId, placeTypeId);
            }

            if (args.Any(a => a.StartsWith("-retag")))
            {
                var arg = args.First(a => a.StartsWith("-retag"));
                string[] pieces = arg.Split(':');
                if (pieces.Length == 3)
                    RetagPlaces(pieces[1], pieces[2]);
                else if (pieces.Length == 2)
                    RetagPlaces(pieces[1], "");
                else
                    RetagPlaces();
            }

            if (args.Any(a => a == "-fudgeIt"))
            {
                FudgeIt();
            }
        }

        private static void LoadEverything()
        {
            //Index checks
            var db = new PraxisContext();
            byte[] pending = "pending".ToByteArrayUTF8();

            if (!db.Places.Any())
            {
                db.DropIndexes();
                db.GlobalData.Add(new GlobalData() { DataKey = "rebuildIndexes", DataValue = pending });
                db.SaveChanges();
            }

            processPmds();
            processPbfs();

            if (db.GlobalData.Any(g => g.DataKey == "rebuildIndexes" && g.DataValue == pending))
            {
                db.RecreateIndexes();
                var entry = db.GlobalData.First(g => g.DataKey == "rebuildIndexes");
                entry.DataValue = "done".ToByteArrayUTF8();
                db.SaveChanges();
            }

            db.ExpireAllMapTiles();
            db.ExpireAllSlippyMapTiles();
        }

        private static void SetEnvValues()
        {
            Log.WriteLog("Setting preferred NET environment variables for performance. A restart may be required for them to apply.");
            System.Environment.SetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT", "1", EnvironmentVariableTarget.Machine);
        }

        private static void PwdSpeedTest()
        {
            Log.WriteLog("Determining the correct value for Rounds on this computer for saving passwords...");
            System.Diagnostics.Stopwatch encryptTimer = new System.Diagnostics.Stopwatch();
            int rounds = 6;
            while (encryptTimer.ElapsedMilliseconds < 250)
            {
                rounds++;
                BCrypt.Net.BCrypt.EnhancedHashPassword("anythingWillDo", rounds);
                encryptTimer.Stop();
                Log.WriteLog("Time with Rounds:" + rounds + ": " + encryptTimer.ElapsedMilliseconds + "ms");

            }
            Log.WriteLog("Suggestion: Set the PasswordRounds configuration variable to " + rounds + " in PraxisMapper's appsettings.json file");
        }

        private static void createDb()
        {
            Log.WriteLog("Creating database with current database settings.");
            using var db = new PraxisContext();
            db.MakePraxisDB();
        }

        private static void processPmds()
        {
            var db = new PraxisContext();
            var settings = db.ServerSettings.FirstOrDefault();
            var bounds = new GeoArea(settings.SouthBound, settings.WestBound, settings.NorthBound, settings.EastBound).ToPolygon().EnvelopeInternal;

            List<string> filenames = System.IO.Directory.EnumerateFiles(config["PbfFolder"], "*.pmd").ToList();
            foreach (string filename in filenames)
            {
                PlaceExport.LoadToDatabase(filename, bounds: bounds);
                File.Move(filename, filename + "done");
            }
        }

        private static void processPbfs()
        {
            List<string> filenames = System.IO.Directory.EnumerateFiles(config["PbfFolder"], "*.pbf").ToList();
            foreach (string filename in filenames)
            {
                Log.WriteLog("Loading " + filename + " at " + DateTime.Now);
                PbfReader r = new PbfReader();
                r.outputPath = config["PbfFolder"];
                r.styleSet = config["TagParserStyleSet"];
                r.processingMode = config["processingMode"]; // "normal" and "center" and "minimize" allowed
                //r.onlyMatchedAreas = config["OnlyTaggedAreas"] == "True";

                if (config["ResourceUse"] == "low")
                {
                    r.lowResourceMode = true;
                }
                else if (config["ResourceUse"] == "high")
                {
                    r.keepAllBlocksInRam = true; //Faster performance, but files use vastly more RAM than they do HD space. 200MB file = ~6GB total RAM last I checked.
                }
                r.ProcessFileV2(filename, long.Parse(config["UseOneRelationID"]));
                File.Move(filename, filename + "done");
            }
        }

        private static void ConvertPBFtoPMD()
        {
            List<string> filenames = System.IO.Directory.EnumerateFiles(config["PbfFolder"], "*.pbf").ToList();
            foreach (string filename in filenames)
            {
                Log.WriteLog("Loading " + filename + " at " + DateTime.Now);
                PbfReader r = new PbfReader();
                r.outputPath = config["PbfFolder"];
                r.styleSet = "mapTiles";  //config["TagParserStyleSet"];
                r.processingMode = "normal";
                r.saveToDB = false;
                //r.onlyMatchedAreas = true; //This is always true, probably don't need this option anymore either.
                r.ProcessFileV2(filename);
                File.Move(filename, filename + "done");
            }
        }



        /// <summary>
        /// This is intended for pulling a single missing entry into an existing database, and does not process to an external file.
        /// </summary>
        /// <param name="entryId"></param>
        private static void LoadOneEntryFromFile(long entryId)
        {
            //Loads a single relation or way from a PBF into a database.
            List<string> filenames = System.IO.Directory.EnumerateFiles(config["PbfFolder"], "*.pbf").ToList();
            foreach (string filename in filenames)
            {
                Log.WriteLog("Loading " + filename + " at " + DateTime.Now);
                PbfReader r = new PbfReader();
                r.styleSet = config["TagParserStyleSet"];
                r.processingMode = config["processingMode"];
                if (config["ResourceUse"] == "low")
                {
                    r.lowResourceMode = true;
                }
                else if (config["ResourceUse"] == "high")
                {
                    r.keepAllBlocksInRam = true; //Faster performance, but files use vastly more RAM than they do HD space. 200MB file = ~6GB total RAM last I checked.
                }

                ICompleteOsmGeo relation = r.LoadOneRelationFromFile(filename, entryId);
                if (relation == null)
                    relation = r.LoadOneWayFromFile(filename, entryId);

                if (relation != null)
                {
                    var place = GeometrySupport.ConvertOsmEntryToPlace(relation, config["TagParserStyleSet"]);
                    using var db = new PraxisContext();
                    db.Places.Add(place);
                    db.SaveChanges();
                    db.ExpireMapTiles(place.ElementGeometry);
                    db.ExpireSlippyMapTiles(place.ElementGeometry);
                    Log.WriteLog("OSM Element imported successfully");
                    return;
                }
            }
            Log.WriteLog("Element " + entryId + " not found in any .pbf file");
        }

        private static void DrawOneImage(string code)
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            TagParser.ApplyTags(memorySource, "mapTiles");
            ImageStats istats = new ImageStats(OpenLocationCode.DecodeValid(code), 1024, 1024);
            var paintOps = MapTileSupport.GetPaintOpsForPlaces(memorySource, "mapTiles", istats);
            File.WriteAllBytes(config["PbfFolder"] + code + ".png", MapTileSupport.DrawPlusCode(code, paintOps));
            sw.Stop();
            Log.WriteLog("image drawn from memory in " + sw.Elapsed);
        }

        private static void DrawPosterOfServer(int xInches = 24, int yInches = 36, int dpi = 300)
        {
            using var db = new PraxisContext();
            var bounds = db.ServerSettings.First();

            var geoArea = new GeoArea(bounds.SouthBound, bounds.WestBound, bounds.NorthBound, bounds.EastBound);
            var iStats = new ImageStats(geoArea, (int)(geoArea.LongitudeWidth * 5000), (int)(geoArea.LatitudeHeight * 5000)); //temp values to scale correctly.
            iStats.ScaleToFit(xInches * dpi, yInches * dpi);
            Log.WriteLog("Loading all places from DB");

            iStats.filterSize *= .5; //For increased accuracy on the bigger image, we're gonna load things that would draw as half a pixel.
            var places = GetPlaces(geoArea, filterSize: iStats.filterSize);

            Log.WriteLog("Generating paint operations");
            var paintOps = MapTileSupport.GetPaintOpsForPlaces(places, "mapTiles", iStats);
            Log.WriteLog("Drawing image");
            var image = MapTiles.DrawAreaAtSize(iStats, paintOps);

            File.WriteAllBytes("ServerPoster.png", image);
            Log.WriteLog("Image saved to disk as ServerPoster.png");
        }

        private static void ApplyConfigValues()
        {
            PraxisContext.connectionString = config["DbConnectionString"];
            PraxisContext.serverMode = config["DbMode"];

            if (config["MapTilesEngine"] == "SkiaSharp")
            {
                var asm = Assembly.LoadFrom(@"PraxisMapTilesSkiaSharp.dll");
                MapTiles = (IMapTiles)Activator.CreateInstance(asm.GetType("PraxisCore.MapTiles"));
            }
            else if (config["MapTilesEngine"] == "ImageSharp")
            {
                var asm2 = Assembly.LoadFrom(@"PraxisMapTilesImageSharp.dll");
                MapTiles = (IMapTiles)Activator.CreateInstance(asm2.GetType("PraxisCore.MapTiles"));
            }
            MapTileSupport.GameTileScale = config["mapTileScaleFactor"].ToInt();
            MapTileSupport.SlippyTileSizeSquare = config["slippyTileSize"].ToInt();
            MapTileSupport.BufferSize = config["AreaBuffer"].ToDouble();

            if (config["processingMode"] == "minimize")
            {
                SimplifyAreas = true; //rounds off points that are within a Cell10's distance of each other. Makes fancy architecture and highly detailed paths less pretty on map tiles, but works for gameplay data.
            }

            Log.Verbosity = (Log.VerbosityLevels)config["LogLevel"].ToInt();

            if (config["KeepElementsInMemory"] == "True")
                memorySource = new List<DbTables.Place>(20000);

            if (config["ResourceUse"] == "low")
                singleThread = true;
        }

        public static void ReadCoastlineWaterPolyShapefile(string shapePath)
        {
            //TODO: this should probably process to the actual DB. Throwing it to a file for now to test pmd logic.
            //NOTE: this requires the WGS84 version of the polygons. the Mercator version is UTM, not the Mercator you saw in school.
            Log.WriteLog("Reading water polygon data from " + shapePath);
            Stopwatch sw = Stopwatch.StartNew();
            string fileBaseName = config["PbfFolder"] + "coastlines.pmd";
            EGIS.ShapeFileLib.ShapeFile sf = new EGIS.ShapeFileLib.ShapeFile(shapePath);
            var recordCount = sf.RecordCount;
            List<Polygon> polygons = new List<Polygon>(recordCount);
            for (int i = 0; i < recordCount; i++)
            {
                var shapeData = sf.GetShapeDataD(i);
                var poly = Converters.ShapefileRecordToPolygon(shapeData);

                polygons.Add(poly);
            }

            //NEXT: condense down entries
            var notShrinking = polygons.Where(a => a.NumPoints != 5).ToList();
            List<Geometry> possiblyShrinkable = polygons.Where(a => a.NumPoints == 5).Select(a => (Geometry)a).ToList(); //All squares with no additional details.
            var resultGeo = (MultiPolygon)NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(possiblyShrinkable);

            polygons.Clear();
            polygons.AddRange(notShrinking);
            polygons.AddRange(resultGeo.Geometries.Select(g => (Polygon)g).ToList());

            var outputData = new PlaceExport("oceanData.pmd");
            outputData.Open();
            int c = 1;
            //NEXT: save to a place item.
            foreach (var poly in polygons)
            {
                DbTables.Place place = new DbTables.Place();
                place.SourceItemID = 100000000000 + c;
                place.SourceItemType = 2;
                place.DrawSizeHint = GeometrySupport.CalculateDrawSizeHint(poly, TagParser.allStyleGroups["mapTiles"]["bgwater"].PaintOperations); //poly.Area / ConstantValues.squareCell11Area; //accurate number
                place.ElementGeometry = poly;
                place.Tags = new List<PlaceTags>() {
                    new PlaceTags() { Key = "bgwater", Value = "praxismapper" },
                    new PlaceTags() { Key = "natural", Value = "water" }};
                outputData.AddEntry(place);
                c++;
            }
            outputData.Close();

            sw.Stop();
            Log.WriteLog("Water polygon data converted to PraxisMapper data file in " + sw.Elapsed);
        }

        public static void ResetFiles(string folder)
        {
            List<string> filenames = System.IO.Directory.EnumerateFiles(folder, "*.*Done").ToList();
            foreach (var file in filenames)
            {
                File.Move(file, file.Substring(0, file.Length - 4));
            }
        }

        private static Dictionary<string, int> GetTerrainIndex(string style = "mapTiles")
        {
            var dict = new Dictionary<string, int>();
            foreach (var entry in TagParser.allStyleGroups[style])
            {
                if (entry.Value.IsGameElement)
                {
                    dict.Add(entry.Key, dict.Count + 1);
                }
            }
            return dict;
        }

        static List<string> GetCellCombos()
        {
            var list = new List<string>(400);
            foreach (var Yletter in OpenLocationCode.CodeAlphabet)
                foreach (var Xletter in OpenLocationCode.CodeAlphabet)
                {
                    list.Add(String.Concat(Yletter, Xletter));
                }

            return list;
        }

        static List<string> GetCell2Combos()
        {
            var list = new List<string>(400);
            foreach (var Yletter in OpenLocationCode.CodeAlphabet.Take(9))
                foreach (var Xletter in OpenLocationCode.CodeAlphabet.Take(18))
                {
                    list.Add(String.Concat(Yletter, Xletter));
                }

            return list;
        }

        public static void MakeOfflineFilesCell8()
        {
            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var terrainDict = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, string>>>>();
            var index = GetTerrainIndex();
            //To avoid creating a new type, I add the index data as its own entry, and put all the data in the first key under "index".
            terrainDict["index"] = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, string>>>();
            terrainDict["index"][String.Join("|", index.Select(i => i.Key + "," + i.Value))] = new ConcurrentDictionary<string, ConcurrentDictionary<string, string>>();

            foreach (var cell2 in GetCell2Combos())
            {
                var place2 = cell2.ToPolygon();
                var placeTest = db.Places.Any(p => p.ElementGeometry.Intersects(place2)); //DoPlacesExist in a single line.
                if (!placeTest)
                    continue;

                terrainDict[cell2] = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, string>>>();
                foreach (var cell4 in GetCellCombos())
                {
                    var place4 = (cell2 + cell4).ToPolygon();
                    var placeTest4 = db.Places.Any(p => p.ElementGeometry.Intersects(place4)); //DoPlacesExist in a single line.
                    if (!placeTest4)
                        continue;

                    terrainDict[cell2][cell4] = new ConcurrentDictionary<string, ConcurrentDictionary<string, string>>();
                    foreach (var cell6 in GetCellCombos())
                    {
                        try
                        {
                            string pluscode6 = cell2 + cell4 + cell6;
                            GeoArea box6 = pluscode6.ToGeoArea();
                            var quickplaces = PraxisCore.Place.GetPlaces(box6);
                            if (quickplaces.Count == 0)
                                continue;

                            terrainDict[cell2][cell4][cell6] = new ConcurrentDictionary<string, string>();

                            //foreach (var place in quickplaces)
                            //if (place.ElementGeometry.Coordinates.Count() > 1000)
                            //place.ElementGeometry = place.ElementGeometry.Intersection(box6.ToPolygon());


                            Parallel.ForEach(GetCellCombos(), (cell8) =>
                            {
                                string pluscode = pluscode6 + cell8;
                                GeoArea box = pluscode.ToGeoArea();
                                var places = PraxisCore.Place.GetPlaces(box, quickplaces);
                                if (places.Count == 0)
                                    return;

                                places = places.Where(p => p.IsGameElement).ToList();
                                if (places.Count == 0)
                                    return;
                                //var terrainInfo = AreaTypeInfo.SearchArea(ref box, ref places);
                                var terrainsPresent = places.Select(p => p.StyleName).Distinct().ToList();
                                //r terrainsPresent = terrainInfo.Select(t => t.data.areaType).Distinct().ToList();

                                if (terrainsPresent.Count > 0)
                                {
                                    string concatTerrain = String.Join("|", terrainsPresent.Select(t => index[t])); //indexed ID of each type.
                                    terrainDict[cell2][cell4][cell6][cell8] = concatTerrain;
                                }
                            });
                            if (terrainDict[cell2][cell4][cell6].IsEmpty)
                                terrainDict[cell2][cell4].TryRemove(cell6, out _);
                        }
                        catch (Exception ex)
                        {
                            Log.WriteLog("error making file for " + cell2 + cell4 + cell6 + ":" + ex.Message);
                        }
                    }
                    if (terrainDict[cell2][cell4].IsEmpty)
                        terrainDict[cell2].TryRemove(cell4, out _);
                    //else
                    //{
                    //    File.WriteAllText(config["PbfFolder"] + cell2 + cell4 + ".json", JsonSerializer.Serialize(terrainDict));
                    //    terrainDict[cell2].TryRemove(cell4, out var xx);
                    //    Log.WriteLog("Made file for " + cell2 + cell4 + " at " + DateTime.Now);
                    //}
                }
                if (terrainDict[cell2].IsEmpty)
                    terrainDict[cell2].TryRemove(cell2, out _);
                else
                {
                    File.WriteAllText(config["PbfFolder"] + cell2 + ".json", JsonSerializer.Serialize(terrainDict));
                    terrainDict.TryRemove(cell2, out _);
                    Log.WriteLog("Made file for " + cell2 + " at " + DateTime.Now);
                }
            }

            //return JsonSerializer.Serialize(terrainDict);
        }

        public static bool IsTerrainPresent(string styleSet, string terrain, string cell)
        {
            //This will be a DB query based on a style that doesn't have a NOT criteria.
            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            var style = TagParser.allStyleGroups[styleSet][terrain].StyleMatchRules;
            var firstSTyle = style.First();

            var queryTag = firstSTyle.Key;
            var queryVals = firstSTyle.Value.Split('|');
            var queryType = firstSTyle.MatchType;

            var area = cell.ToPolygon();
            var results = db.PlaceTags.Include(p => p.Place).Any(p => p.Key == queryTag && queryVals.Contains(p.Value));
            //this could be done on a list of places



            return false;
        }

        public static void SetDefaultPasswords()
        {
            //expected to be run when in the same folder as PraxisMapper.exe and it's appsetings.json file.
            //May also make a self-signed cert for testing purposes.  System.Security.Cryptography.X509Certificates.CertificateRequest.

            if (!File.Exists("appsettings.json"))
                return;

            var fileText = File.ReadAllText("appsettings.json");
            var fileParts = fileText.Split("setThisPassword");
            var newFileText = "";
            for (int i = 0; i < fileParts.Length - 1; i++)
                newFileText += fileParts[i] + Guid.NewGuid();
            newFileText += fileParts.Last();
            File.WriteAllText("appsettings.json", newFileText);
        }

        public static void RecalcDrawSizeHints()
        {
            //TODO: write something that lets me quick and easy batch commands on the entities.
            using var db = new PraxisContext();
            var groupsDone = 0;
            var groupSize = 10000;
            bool keepGoing = true;
            long lastEntry = 0;
            while (keepGoing)
            {
                var places = db.Places.Include(p => p.Tags).Where(p => db.Places.OrderBy(pp => pp.Id).Where(pp => pp.Id > lastEntry).Select(pp => pp.Id).Take(groupSize).Contains(p.Id)).ToList();
                if (places.Count < groupSize)
                    keepGoing = false;

                lastEntry = places.Max(p => p.Id);

                foreach (var place in places)
                {
                    var newHint = GeometrySupport.CalculateDrawSizeHint(TagParser.ApplyTags(place, "mapTiles"));
                    if (newHint != place.DrawSizeHint)
                        place.DrawSizeHint = newHint;
                }
                db.SaveChanges();
                Log.WriteLog("Checked " + groupSize * (groupsDone + 1) + " entries");
                groupsDone++;
            }
        }

        public static void BatchOp(Action<DbTables.Place> a)
        {
            using var db = new PraxisContext();
            var groupsDone = 0;
            var groupSize = 1000;
            bool keepGoing = true;
            while (keepGoing)
            {
                var places = db.Places.Include(p => p.Tags).Where(p => p.DrawSizeHint > 4000).Skip(groupsDone * groupSize).Take(groupSize).ToList();
                if (places.Count < groupSize)
                    keepGoing = false;

                foreach (var place in places)
                {
                    a(place);
                }
                Log.WriteLog("Saving " + groupSize + " changes");
                db.SaveChanges();
                groupsDone++;
            }
        }

        public Action<DbTables.Place> CalcDrawSizeHint = (p) => { p.DrawSizeHint = GeometrySupport.CalculateDrawSizeHint(TagParser.ApplyTags(p, "mapTiles")); };
        public Action<DbTables.Place> ReduceSize = (place) =>
        {
            place.ElementGeometry = Singletons.reducer.Reduce(NetTopologySuite.Simplify.TopologyPreservingSimplifier.Simplify(place.ElementGeometry, ConstantValues.resolutionCell10));

        };

        public static void ExtractSubMap(long parentPlace, long parentPlaceType)
        {
            Stopwatch sw = new Stopwatch();
            sw.Start();
            //Given the id/type of a parent Place (presumably an AdminBound, but not necessarily), extract all places that interesect to a text file(s).
            var db = new PraxisContext();

            var parent = db.Places.Where(p => p.SourceItemID == parentPlace && p.SourceItemType == parentPlaceType).FirstOrDefault();
            if (parent == null)
            {
                Log.WriteLog("Place " + parentPlace + " not found in database, not extracting map.");
                return;
            }

            int runningcount = 0;
            int take = 1000; //test value.
            long maxID = 0;
            bool keepGoing = true;
            PlaceExport export = new PlaceExport(config["PbfFolder"] + parentPlace + "-submap.pmd");

            while (keepGoing)
            {
                var allPlaces = db.Places.Include(p => p.Tags).Where(p => p.Id < maxID && p.ElementGeometry.Intersects(parent.ElementGeometry)).Take(take).ToList();
                if (allPlaces.Count < take)
                    keepGoing = false;

                maxID = allPlaces.Max(p => p.Id);
                runningcount += allPlaces.Count;
                foreach (var p in allPlaces)
                {
                    export.AddEntry(p);
                }
                export.WriteToDisk();
                Log.WriteLog(runningcount + " items written to file total so far.");
            }
            sw.Stop();
            Log.WriteLog(parentPlace + "-submap files written to OutputData folder at " + DateTime.Now + ", completed in " + sw.Elapsed);
        }

        public static void RetagPlaces(string styleSet = "", string style = "")
        {
            //In the DB, load up places, run TagParser on them for multiple styles, and save the results to PlaceData
            //This will allow for faster searching for specific styles by using the PlaceData indexes instead of loading all
            //places in the geospatial index (which usually requires loading up countries) and THEN tag-parsing them.
            //This must be done before other PlaceData fields are added for batch purposes.
            Log.WriteLog("Starting to re-tag places at " + DateTime.Now);

            //TODO: this really doesn't need the geometry, it COULD just scan PlaceTags instead of Places if it inserted entries by id

            var db = new PraxisContext();
            db.Database.SetCommandTimeout(600); //while i'm still loading geometry, some entries are huge and need a big timeout to succeed.

            long skip = 0;
            int take = 1000; //test value.
            bool keepGoing = true;

            Stopwatch sw = new Stopwatch();
            while (keepGoing)
            {
                sw.Restart();
                //using the last ID is way more efficient than Skip(int). TODO Apply this anywhere else in the app I use skip/take
                var placeQuery = db.Places.Include(p => p.PlaceData).Where(p => p.Id > skip);
                if (!String.IsNullOrWhiteSpace(styleSet))
                {
                    if (style == "")
                        placeQuery = placeQuery.Where(p => p.PlaceData.Any(d => d.DataKey == styleSet));
                    else
                    {
                        var styleBytes = style.ToByteArrayUTF8();
                        placeQuery = placeQuery.Where(p => p.PlaceData.Any(d => d.DataKey == styleSet && d.DataValue == styleBytes));
                    }

                }
                var allPlaces = placeQuery.Take(take).ToList();
                if (allPlaces.Count < take)
                    keepGoing = false;

                skip = allPlaces.Max(p => p.Id);
                foreach (var p in allPlaces)
                {
                    p.Tags = db.PlaceTags.Where(t => t.SourceItemType == p.SourceItemType && t.SourceItemId == p.SourceItemID).ToList();
                    PraxisCore.Place.PreTag(p);
                }

                db.SaveChanges();
                db.ChangeTracker.Clear();
                sw.Stop();
                Log.WriteLog(allPlaces.Count + " places tagged in " + sw.ElapsedMilliseconds + "ms");
            }
            Log.WriteLog("Retag Complete at " + DateTime.Now);
        }

        public static void FudgeIt()
        {
            //Adjust some values to make things work better in general for apps

            var db = new PraxisContext();

            //1: Replace "United States of America" (148838) with "Continental United States" (9331155)
            //Reason: Allows for thumbnails of the US that don't require drawing the entire world. 
            //Sorry Alaska, Hawaii, Guam, and Puerto Rico, you'll have to be happy without a level 2 adminBound over you.
            var us = db.Places.Include(p => p.PlaceData).FirstOrDefault(p => p.SourceItemID == 148838 && p.SourceItemType == 3);
            var cus = db.Places.Include(p => p.PlaceData).FirstOrDefault(p => p.SourceItemID == 9331155 && p.SourceItemType == 3);

            if (us != null && cus != null)
            {
                var swapData = us.PlaceData;
                cus.PlaceData.Clear();

                foreach (var sd in swapData)
                {
                    us.PlaceData.Remove(sd);
                    cus.PlaceData.Add(new PlaceData() { DataKey = sd.DataKey, DataValue = sd.DataValue });
                }
            }

            //This is the historic boundary for Ireland, which is not the admin boundary, and I dont want to treat a whole country as
            //1 historic place for gameplay purposes.
            var irl = db.Places.Include(p => p.PlaceData).FirstOrDefault(p => p.SourceItemID == 13428950 && p.SourceItemType == 3);
            if (irl != null)
            {
                db.Places.Remove(irl);
            }

            db.SaveChanges();
        }

        public class OfflineDataV2
        {
            public string PlusCode { get; set; }
            public List<OfflinePlaceEntry> entries { get; set; }
            public Dictionary<int, string> nameTable { get; set; } //id, name
            public Dictionary<string, int> gridNames { get; set; } //pluscode, nameTable entry id
        }

        public class OfflinePlaceEntry
        {
            public int? nid { get; set; } = null; //nametable id
            public int tid { get; set; } //terrain id, which style entry this place is
            public int gt { get; set; } //geometry type. 1 = point, 2 = line OR hollow shape, 3 = filled shape.
            //public string geometry { get; set; }
            public string p { get; set; } //Points, local to the given PlusCode
        }
        public static void MakeOfflineJson(string plusCode, Polygon bounds = null, bool saveToFile = true)
        {
            //Make offline data for PlusCode6s, repeatedly if the one given is a 4 or 2.
            var styleSet = "mapTiles";


            if (bounds == null)
            {
                var dbB = new PraxisContext();
                var settings = dbB.ServerSettings.FirstOrDefault();
                bounds = new GeoArea(settings.SouthBound, settings.WestBound, settings.NorthBound, settings.EastBound).ToPolygon();
                dbB.Dispose();
            }

            var area = plusCode.ToPolygon();
            if (!area.Intersects(bounds))
                return;

            if (plusCode.Length < 6)
            {
                if (!PraxisCore.Place.DoPlacesExist(plusCode.ToGeoArea()))
                    return;

                if (plusCode.Length == 4)
                {
                    ParallelOptions po = new ParallelOptions();
                    po.MaxDegreeOfParallelism = 4;
                    Parallel.ForEach(GetCellCombos(), po, pair =>
                    {
                        MakeOfflineJson(plusCode + pair, bounds, saveToFile);
                    });

                    return;
                }
                else
                {
                    var doneCell2s = "0";
                    if (File.Exists("lastOfflineEntry.txt"))
                        doneCell2s = File.ReadAllText("lastOfflineEntry.txt");
                    if (doneCell2s.Contains(plusCode) && plusCode != "")
                        return;

                    foreach (var pair in GetCellCombos())
                        MakeOfflineJson(plusCode + pair, bounds, saveToFile);
                    File.AppendAllText("lastOfflineEntry.txt", "|" + plusCode);
                    return;
                }
            }

            //This is to let us be resumable if this stop for some reason.
            //+ "\\" + plusCode.Substring(2, 2) +
            if (File.Exists(config["PbfFolder"] + plusCode.Substring(0,2) + "\\" + plusCode + ".json"))
                return;

            Directory.CreateDirectory(config["PbfFolder"] + plusCode.Substring(0, 2));

            var sw = Stopwatch.StartNew();
            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            var cell8 = plusCode.ToGeoArea();
            var cell8Poly = cell8.ToPolygon();
            var placeData = PraxisCore.Place.GetPlaces(cell8, styleSet: styleSet, dataKey: styleSet, skipTags: true);
            int nameCounter = 1; //used to determine nametable key

            if (placeData.Count == 0)
                return;

            List<OfflinePlaceEntry> entries = new List<OfflinePlaceEntry>(placeData.Count);
            Dictionary<string, int> nametable = new Dictionary<string, int>(); //name, id

            //List<DbTables.Place> toRemove = new List<DbTables.Place>(); //not worth the effort. tiny percentage of places.

            var min = cell8.Min;
            foreach (var place in placeData)
            {
                place.ElementGeometry = place.ElementGeometry.Intersection(cell8Poly);
                place.ElementGeometry = place.ElementGeometry.Simplify(ConstantValues.resolutionCell11Lon);
                if (place.ElementGeometry.IsEmpty)
                {
                    //toRemove.Add(place);
                    continue; //Probably an element on the border thats getting pulled in by buffer.
                }

                //var name = TagParser.GetName(place);
                int? nameID = null;
                if (!string.IsNullOrWhiteSpace(place.Name))
                {
                    if (!nametable.TryGetValue(place.Name, out var nameval))
                    {
                        nameval = nameCounter;
                        nametable.Add(place.Name, nameval);
                        nameCounter++;
                        //attach to this item.
                        nameID = nameval;
                    }
                }

                var style = TagParser.allStyleGroups[styleSet][place.StyleName];

                //I'm locking these geometry items to a tile, So I convert these points in the geometry to integers, effectively
                //letting me draw Cell11 pixel-precise points from this info, and is shorter stringified for JSON vs floats/doubles.
                var coordSets = GetCoordEntries(place, cell8.Min);
                foreach (var coordSet in coordSets)
                {
                    if (coordSet == "")
                        continue;
                    //System.Diagnostics.Debugger.Break();
                    var offline = new OfflinePlaceEntry();
                    offline.nid = nameID;
                    offline.tid = style.MatchOrder; //Client will need to know what this ID means from the offline style endpoint output.

                    offline.gt = place.ElementGeometry.GeometryType == "Point" ? 1 : place.ElementGeometry.GeometryType == "LineString" ? 2 : style.PaintOperations.All(p => p.FillOrStroke == "stroke") ? 2 : 3;
                    offline.p = coordSet;
                    entries.Add(offline);
                }
            }

            //placeData = placeData.Except(toRemove).ToList();

            //removing this, because the client should be able to draw names as a color on an image with the available data now.
            //a nametable is entirely unnecessary
            //var cell82 = (GeoArea)cell8;
            //var terrainInfo = AreaStyle.GetAreaDetailsParallel(cell82, placeData);
            //var stringSize = plusCode.Length;
            //Dictionary<string, int> nameInfo = terrainInfo.Where(t => t.data.name != "").Select(t => new { t.plusCode, nameId = nametable[t.data.name] }).ToDictionary(k => k.plusCode.Substring(stringSize), v => v.nameId);
            //TODO: work out some special values for nameInfo here I can assign names to a square instead of each individual cell
            //That will dramatically reduce nametable storage space.
            //opt1: if all entries start with the same plusCode and occupy that entire Cell, just use that value (removes 400 entries).
            //opt2: add new values to a new object that indicate 2 corners of a square that hold the name in question. This is MORE data if each square is unique
            //but much less if it's a big block.
            //opt3: have the client use the geoData to 'draw' a table with the values attached to each item. This can be done separately from the graphics using the same data.
            //but would need a way to map names to colors or something.


            var finalData = new OfflineDataV2();
            finalData.PlusCode = plusCode;
            finalData.nameTable = nametable.Count > 0 ? nametable.ToDictionary(k => k.Value, v => v.Key) : null;
            finalData.entries = entries;
            //finalData.gridNames = nameInfo;

            JsonSerializerOptions jso = new JsonSerializerOptions();
            jso.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            string data = JsonSerializer.Serialize(finalData, jso);

            if (saveToFile)
            {
                //+ "\\" + plusCode.Substring(2, 2) +
                File.WriteAllText(config["PbfFolder"] + plusCode.Substring(0,2) + "\\" + plusCode + ".json", data);
            }
            else
            {
                GenericData.SetAreaData(plusCode, "offlineV2", data);
            }
            sw.Stop();
            Log.WriteLog("Created and saved offline data for " + plusCode + " in " + sw.Elapsed);

        }


        public static List<string> GetCoordEntries(DbTables.Place place, GeoPoint min)
        {
            List<string> points = new List<string>();

            if (place.ElementGeometry.GeometryType == "MultiPolygon")
            {
                foreach (var poly in ((MultiPolygon)place.ElementGeometry).Geometries) //This should be the same as the Polygon code below.
                {
                    points.AddRange(GetPolygonPoints(poly as Polygon, min));
                }
            }
            else if (place.ElementGeometry.GeometryType == "Polygon")
            {
                points.AddRange(GetPolygonPoints(place.ElementGeometry as Polygon, min));
            }
            else
                points.Add(string.Join("|", place.ElementGeometry.Coordinates.Select(c => (int)((c.X - min.Longitude) / ConstantValues.resolutionCell11Lon) + "," + ((int)((c.Y - min.Latitude) / ConstantValues.resolutionCell11Lat)))));

            if (points.Count == 0)
            {
                //System.Diagnostics.Debugger.Break();
            }

            return points;
        }

        public static List<string> GetPolygonPoints(Polygon p, GeoPoint min)
        {
            List<string> results = new List<string>();
            if (p.Holes.Length == 0)
                results.Add(string.Join("|", p.Coordinates.Select(c => (int)((c.X - min.Longitude) / ConstantValues.resolutionCell11Lon) + "," + ((int)((c.Y - min.Latitude) / ConstantValues.resolutionCell11Lat)))));
            else
            {
                //Split this polygon into smaller pieces, split on the center of each hole present longitudinally
                //West to east direction chosen arbitrarily.
                var westEdge = p.Coordinates.Min(c => c.X);
                var northEdge = p.Coordinates.Max(c => c.Y);
                var southEdge = p.Coordinates.Min(c => c.Y);

                List<double> splitPoints = new List<double>();
                foreach (var hole in p.Holes.OrderBy(h => h.Centroid.X))
                    splitPoints.Add(hole.Centroid.X);

                foreach (var point in splitPoints)
                {
                    try
                    {
                        var splitPoly = new GeoArea(southEdge, westEdge, northEdge, point).ToPolygon();
                        var subPoly = p.Intersection(splitPoly);

                        //Still need to check that we have reasonable geometry here.
                        if (subPoly.GeometryType == "Polygon")
                            results.AddRange(GetPolygonPoints(subPoly as Polygon, min));
                        else if (subPoly.GeometryType == "MultiPolygon")
                        {
                            foreach (var p2 in ((MultiPolygon)subPoly).Geometries)
                                results.AddRange(GetPolygonPoints(p2 as Polygon, min));
                        }
                        else
                            Log.WriteLog("Offline proccess error: Got geoType " + subPoly.GeometryType + ", which wasnt expected");
                    }
                    catch(Exception ex)
                    {
                        Log.WriteLog("Offline proccess error: " + ex.Message);
                    }
                    westEdge = point;
                }
            }
            return results.Distinct().ToList(); //In the unlikely case splitting ends up processing the same part twice
        }

    }
}
