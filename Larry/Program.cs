using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NetTopologySuite.Geometries;
using OsmSharp.Complete;
using OsmSharp.IO.PBF;
using PraxisCore;
using PraxisCore.PbfReader;
using PraxisCore.Support;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
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

        static void Main(string[] args)
        {
            SetEnvValues();
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

            if (args.Any(a => a == "-openCellId"))
            {
                ConvertOpenCellIdToPMD();
            }

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
                SetDefaultPasswords();
                var db = new PraxisContext();
                TagParser.Initialize(true, null);
                createDb();
                LoadEverything();
                if (config["processingMode"] != "offline")
                    db.SetServerBounds(long.Parse(config["UseOneRelationID"]));
                Log.WriteLog("Server setup complete in " + sw.Elapsed);
            }

            if (args.Any(a => a == "-resetStyles"))
            {
                using var db = new PraxisContext();
                db.ResetStyles();
            }

            TagParser.Initialize(config["ForceStyleDefaults"] == "True", MapTiles);            

            if (args.Any(a => a.StartsWith("-convertPbfs")))
            {
                ConvertPBFtoPMD();
            }

            if (args.Any(a => a.StartsWith("-addOneElement:")))
            {
                var vals = args.First(a => a.StartsWith("-addOneElement:")).Split(':');
                LoadOneEntryFromFile(vals[1].ToLong());
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

            //TODO: this should indicate it wants the WSG84 files, not the Mercator ones.
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

            if (args.Any(a => a == "-makeOfflineFiles"))
            {
                var sw = Stopwatch.StartNew();
                if (!File.Exists("lastOfflineEntry.txt")) //Dont overwrite existing file, that means we're resuming an interrupted run.
                    File.WriteAllText("lastOfflineEntry.txt", "");
                OfflineData.simplifyRes = config["offlineSimplifyResolution"].ToDouble();
                OfflineData.xRes = config["offlineXPixelResolution"].ToDouble();
                OfflineData.yRes = config["offlineYPixelResolution"].ToDouble();
                OfflineData.styles = config["offlineStyleSets"].Split(",");
                OfflineData.filePath = config["PbfFolder"];

                OfflineData.MakeOfflineJsonFromOfflineTable(""); //Larry should prefer this, since thats its purpose.
                File.Delete("lastOfflineEntry.txt");
                sw.Stop();
                Log.WriteLog("Creating offline data completed in " + sw.Elapsed);
            }

            if (args.Any(a => a == "-makeMinimizedFiles"))
            {
                if (!File.Exists("lastOfflineEntry.txt")) //Dont overwrite existing file, that means we're resuming an interrupted run.
                    File.WriteAllText("lastOfflineEntry.txt", "");
                OfflineData.filePath = config["PbfFolder"];
                OfflineData.styles = ["suggestedmini"]; //Fixed for minimized mode, along with most other variables.

                OfflineData.MakeMinimizedOfflineData("");
                File.Delete("lastOfflineEntry.txt");

                ZipFolders();
            }

            if (args.Any(a => a == "-mergeOfflineSets"))
            {
                MergeOfflineSets(config["PbfFolder"]);
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

            //TODO: rename this to something else. I know this is PlaceData but this sure sounds like Tags are involved.
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

            if (args.Any(a => a == "-loadAsOffline"))
            {
                LoadAsOffline();
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
            try
            {
                System.Environment.SetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT", "1", EnvironmentVariableTarget.Machine);
            }
            catch
            {
                //Quietly continue on.
            }
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
            //TODO: may need a config option to determine if bounds are ignored, using server setting, or using polygon value from the config?
            var bounds = new GeoArea(settings.SouthBound, settings.WestBound, settings.NorthBound, settings.EastBound).ToPolygon().EnvelopeInternal;

            List<string> filenames = System.IO.Directory.EnumerateFiles(config["PbfFolder"], "*.pmd").ToList();
            foreach (string filename in filenames)
            {
                try
                {
                    PlaceExport.LoadToDatabase(filename, config["processingMode"], bounds, config["TagParserStyleSet"]);
                    File.Move(filename, filename + "done");
                }
                catch (Exception ex)
                {
                    Log.WriteLog("File " + filename + " was not processed: " + ex.Message);
                }
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
                r.styleSet = config["TagParserStyleSet"];
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

            Log.WriteLog("Saving processed shaped to PMD file...");
            var outputData = new PlaceExport("oceanData.pmd");
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
            if (File.Exists("lastRetaggedId.txt"))
            {
                var lastRetagged = File.ReadAllText("lastRetaggedId.txt");
                skip = lastRetagged.ToLong();
            }

            int take = 4000; //test value.
            bool keepGoing = true;

            Stopwatch sw = new Stopwatch();
            while (keepGoing)
            {
                sw.Restart();
                //using the last ID is way more efficient than Skip(int). TODO Apply this anywhere else in the app I use skip/take
                var placeQuery = db.Places.Include(p => p.Tags).Include(p => p.PlaceData).Where(p => p.Id > skip);
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


                var newSkip = allPlaces.Max(p => p.Id);
                skip = newSkip;

                foreach (var p in allPlaces)
                //Parallel.ForEach(allPlaces, (p) =>
                {
                    PraxisCore.Place.PreTag(p);
                    db.PlaceData.UpdateRange(p.PlaceData); //This is necessary, and blocks parallel operations because its on the EF context.
                } //); 

                var changes = db.SaveChanges();
                db.ChangeTracker.Clear();
                File.WriteAllText("lastRetaggedId.txt", skip.ToString());
                sw.Stop();
                Log.WriteLog(allPlaces.Count + " places tagged in " + sw.ElapsedMilliseconds + "ms   (" + (allPlaces.Count + skip).ToString() + " total done.)");
            }
            Log.WriteLog("Retag Complete at " + DateTime.Now);
            File.Delete("lastRetaggedId.txt");
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

            //Aleutian Islands Wilderness area is 750+ polygons in a multipolygon. MariaDB does not like Intersects() checks on this.
            var aiwa = db.Places.Include(p => p.PlaceData).FirstOrDefault(p => p.SourceItemID == 16685152 && p.SourceItemType == 3);
            if (aiwa != null)
            {
                //TODO: might need to copy/clone tags and placedata instead of just assigning.
                var newPlaces = ((MultiPolygon)aiwa.ElementGeometry).Select(p => new DbTables.Place() { ElementGeometry = p, Name = aiwa.Name, PlaceData = aiwa.PlaceData, Tags = aiwa.Tags, SourceItemID = aiwa.SourceItemID, SourceItemType = aiwa.SourceItemType });
                foreach (var np in newPlaces)
                    np.DrawSizeHint = GeometrySupport.CalculateDrawSizeHint(np, "mapTiles");

                db.Places.AddRange(newPlaces);
                db.Places.Remove(aiwa);
            }

            db.SaveChanges();
        }

        public static void PruneFolders(string basepath)
        {
            var folders = Directory.GetDirectories(basepath);
            foreach (var folder in folders)
            {
                PruneFolders(folder);
            }

            var folderCount = Directory.GetDirectories(basepath).Length;
            var fileCount = Directory.GetFiles(basepath, "*").Length;

            if (folderCount == 0 && fileCount == 0)
                Directory.Delete(basepath, false);
        }

        public static void MergeOfflineSets(string parentFolder)
        {
            //given D:\data\merging\, expect stuff like D:\Data\merging\maptiles\22\2223.zip
            var folders = Directory.GetDirectories(parentFolder);

            Console.WriteLine("merging files into " + folders[0] + " files");
            var writeFolder = folders[0];

            EnumerationOptions eo = new EnumerationOptions() { RecurseSubdirectories = true };
            var allZipFiles = Directory.EnumerateFiles(parentFolder, "*.zip", eo).ToList();

            var existingFiles = allZipFiles.Select(f => Path.GetFileName(f)).Distinct().ToList();
            JsonSerializerOptions jso = new JsonSerializerOptions();
            jso.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;

            foreach (var zip in existingFiles)
            {
                Console.WriteLine("Merging " + zip);
                var finalEntries = new Dictionary<string, OfflineData.OfflineDataV2>();
                //var zipFileA = new ZipArchive(File.Open(zip, FileMode.Open), ZipArchiveMode.Update);
                //foreach (var entry in zipFileA.Entries)
                //{
                //    var streamA = entry.Open();
                //    var dataA = JsonSerializer.Deserialize<OfflineData.OfflineDataV2>(streamA);
                //    finalEntries.Add(entry.Name, dataA);
                //    streamA.Close();
                //    streamA.Dispose();
                //}
                //zipFileA.Dispose();

                foreach (var folder in folders)
                {
                    var subPath = folder + "\\" + zip.Substring(0,2) + "\\" + zip;
                    if (File.Exists(subPath))
                    {
                        FileStream filestream = null;
                        ZipArchive zipFileB = null;
                        try
                        {
                            filestream = File.Open(subPath, FileMode.Open);
                            zipFileB = new ZipArchive(filestream, ZipArchiveMode.Read);

                            var mergingEntries = zipFileB.Entries.Where(e => finalEntries.ContainsKey(e.Name));
                            foreach (var entry in mergingEntries)
                            {
                                var streamB = entry.Open();
                                var dataB = JsonSerializer.Deserialize<OfflineData.OfflineDataV2>(streamB);
                                streamB.Close();
                                streamB.Dispose();

                                var dataA = finalEntries[entry.Name];
                                dataA = OfflineData.MergeOfflineFiles(dataA, dataB);
                            }

                            mergingEntries = zipFileB.Entries.Where(e => !finalEntries.ContainsKey(e.Name));
                            foreach (var entry in mergingEntries)
                            {
                                var streamC = entry.Open();
                                var dataC = JsonSerializer.Deserialize<OfflineData.OfflineDataV2>(streamC);
                                streamC.Close();
                                streamC.Dispose();
                                finalEntries.Add(entry.Name, dataC);
                            }
                        }
                        catch(Exception ex)
                        {
                            Log.WriteLog("File " + subPath + " failed to read and merge, zip probably corrupt");
                        }
                        finally
                        {
                            if (zipFileB != null) zipFileB.Dispose();
                            if (filestream != null)
                            {
                                filestream.Close(); filestream.Dispose();
                            }
                        }
                    }
                }
                //Write new file.
                var outFile = writeFolder + "\\" + zip.Substring(0, 2) + "\\" + zip;
                if (File.Exists(outFile))
                    File.Delete(outFile);
                var destParentFolder = Path.GetDirectoryName(outFile);
                if (!Directory.Exists(destParentFolder))
                    Directory.CreateDirectory(destParentFolder);

                var newZip = ZipFile.Open(outFile, ZipArchiveMode.Create);
                var timeGenerated = DateTime.UtcNow.ToUnixTime();
                foreach (var entry in finalEntries)
                {
                    entry.Value.dateGenerated = timeGenerated;
                    var e = newZip.CreateEntry(entry.Key).Open();
                    using (var streamWriter = new StreamWriter(e))
                        streamWriter.Write(JsonSerializer.Serialize(entry.Value, jso));
                    e.Close();
                    e.Dispose();
                }
                newZip.Dispose();
            }
        }

        public static void LoadAsOffline()
        {
            List<string> filenames = System.IO.Directory.EnumerateFiles(config["PbfFolder"], "*.pbf").ToList();
            foreach (string filename in filenames)
            {
                Log.WriteLog("Loading " + filename + " at " + DateTime.Now);
                PbfReader r = new PbfReader();
                r.outputPath = config["PbfFolder"];
                r.styleSet = config["TagParserStyleSet"];
                r.processingMode = "offline";
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

        //For zipping up the output from making minimized offline data
        public static void ZipFolders()
        {
            Log.WriteLog("Zipping all folders");
            var folders = Directory.EnumerateDirectories(config["PbfFolder"]);

            foreach (string folder in folders)
            {
                ZipFile.CreateFromDirectory(folder, config["PbfFolder"] + Path.GetFileName(folder) + ".zip", CompressionLevel.SmallestSize, false);
                Log.WriteLog(folder + " zipped");
            }
            Log.WriteLog("Zipping files complete");
        }

        public static void ConvertOpenCellIdToPMD()
        {
            Log.WriteLog("Starting conversion from OpenCellId CSV to PMD file at " + DateTime.Now);
            //Load the CSV file from OpenCellID
            var sourceCsv = File.ReadAllLines(config["PbfFolder"] + "openCellId.csv").Skip(1);
            //Translate it to PMD entries (Points, with a tag for Radius and a Style:OpenCellId)
            var cellTowerPmd = new PlaceExport(config["PbfFolder"] + "openCellId.pmd");
            var counter = 200000000000; //Ocean entries start at 100000000000, cell towers start at 200000000000

            foreach (var item in sourceCsv)
            {
                counter++;
                var data = item.Split(",");
                var lon = data[6];
                var lat = data[7];
                var radius = data[8]; //assuming this is meters, not feet.

                var place = new DbTables.Place() {
                    SourceItemID = counter,
                    SourceItemType = 2, //its a way now, not a point.
                    //DrawSizeHint = GetDrawSizeHint(),
                    
                    ElementGeometry = new Point(lon.ToDouble(), lat.ToDouble()).Buffer(radius.ToDouble() * ConstantValues.oneMeterLat),
                    Tags = new List<PlaceTags>() { new PlaceTags() { Key = "openCellId", Value = "yes" } }
                };

                place.DrawSizeHint = (radius.ToDouble() * radius.ToDouble() * Math.PI) / ConstantValues.squareCell11Area;

                cellTowerPmd.AddEntry(place);
            }

            //Save to file.
            Log.WriteLog("Saving data...");
            cellTowerPmd.WriteToDisk();
            Log.WriteLog("Conversion completed at " + DateTime.Now);
        }
    }
}
