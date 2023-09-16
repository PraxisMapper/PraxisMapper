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
using System.Security.AccessControl;
using System.Text;
using System.Text.Json;
using System.Threading;
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

            if (args.Any(a => a == "-resetPbf"))
            {
                ResetFiles(config["PbfFolder"]);
            }

            if (args.Any(a => a == "-resetGeomData"))
            {
                ResetFiles(config["OutputDataFolder"]);
            }

            if (args.Any(a => a == "-rollDefaultPasswords"))
            {
                SetDefaultPasswords();
            }

            if (!args.Any(a => a == "-makeServerDb")) //This will not be available until after creating the DB slightly later.
                TagParser.Initialize(config["ForceStyleDefaults"] == "True", MapTiles); //This last bit of config must be done after DB creation check

            if (args.Any(a => a == "-processPbfs"))
            {
                processPbfs();
            }

            if (args.Any(a => a == "-loadProcessedData"))
            {
                loadProcessedData();
            }

            //This is the single command to get a server going, assuming you have done all the setup steps yourself beforehand and your config is correct. 
            //NOTE: the simplest setup possible is to grab map data and run PraxisMapper.exe directly now, and this is the 2nd best choice now.
            if (args.Any(a => a == "-makeServerDb"))
            {
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                SetEnvValues();
                SetDefaultPasswords();
                using var db = new PraxisContext();
                TagParser.Initialize(true, null);
                createDb();
                processPbfs();
                if (config["UseGeomDataFiles"] == "True")
                    loadProcessedData();
                db.SetServerBounds(long.Parse(config["UseOneRelationID"]));
                Log.WriteLog("Server setup complete in " + sw.Elapsed);
            }

            if (args.Any(a => a == "-makeWholeServer")) //Not a release 1 feature, but taking notes now.
            {
                SetEnvValues();
                //This is the wizard command, try to check and do everything at once.
                Log.WriteLog("Checking for installed DB per config (" + config["DbMode"] + ")");
                PraxisContext db;
                try
                {
                    db = new PraxisContext();
                }
                //Specific exceptions should hint at what to do, a general one covers ones I dont know how to handle.
                catch (Exception ex)
                {
                    Log.WriteLog("Hit an error checking for the existing database that I'm not sure how to handle:" + ex.Message);
                    return;
                }

                Log.WriteLog("Creating the Praxis DB per the connection string...");
                try
                {
                    createDb();
                }
                catch (Exception ex)
                {
                    //figure out why i can't create. Probably account settings?
                }

                PwdSpeedTest();


                //Check for MariaDB and install/configure if missing (including service account)
                //check for a PBF file and prompt to download one if none found
                //if data files are present, use them. otherwise process the PBF file per settings
                //Pre-generate gameplay map tiles, but present it as an option. It's faster to do it ahead of time but uses up more DB space if you aren't gonna need them all immediately.
                //Possible: Grab the Solar2D example app, adjust it to work with the server running on this machine.
                //--check external IP, update .lua source file to point to this pc.
                //Fire up the Kestral exe to get the server working
                //Open up a browser to the adminview slippytile page.
                //}
            }

            if (args.Any(a => a == "-resetStyles"))
            {
                using var db = new PraxisContext();
                db.ResetStyles();
            }

            TagParser.Initialize(config["ForceStyleDefaults"] == "True", MapTiles);

            if (args.Any(a => a == "-loadOfflineJson"))
            {
                LoadOfflineDataToDb(config["PbfFolder"]);
            }

            //NOTE: this seems to drop out a lot of geometry, so I may not want to support this after all.
            if (args.Any(a => a.StartsWith("-shrinkFiles") || a.StartsWith("-reprocessFiles")))
            {
                List<string> filenames = System.IO.Directory.EnumerateFiles(config["PbfFolder"], "*.geomData").ToList();
                foreach (string filename in filenames)
                {
                    Log.WriteLog("Loading " + filename + " at " + DateTime.Now);
                    PbfReader r = new PbfReader();
                    r.outputPath = config["OutputDataFolder"];
                    r.processingMode = config["processingMode"]; // "normal" and "center" and 'minimize' allowed
                    r.saveToDB = false; //we want these as separate files for later.
                    r.onlyMatchedAreas = config["OnlyTaggedAreas"] == "True";
                    r.reprocessFile = true;

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

            if (args.Any(a => a.StartsWith("-splitPbfByStyle:")))
            {
                var style = args.First(a => a.StartsWith("-splitPbfByStyle:")).Split(':')[1];
                List<string> filenames = System.IO.Directory.EnumerateFiles(config["PbfFolder"], "*.pbf").ToList();
                foreach (string filename in filenames)
                {
                    Log.WriteLog("Loading " + filename + " at " + DateTime.Now);
                    PbfReader r = new PbfReader();
                    r.outputPath = config["OutputDataFolder"];
                    r.styleSet = style;
                    r.processingMode = config["processingMode"]; // "normal" and "center" allowed
                    r.saveToDB = false; //we want these as separate files for later.
                    r.onlyMatchedAreas = config["OnlyTaggedAreas"] == "True";
                    r.reprocessFile = config["reprocessFiles"] == "True";
                    r.splitByStyleSet = true; //Implies saving to geomdata files.

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

            if (args.Any(a => a.StartsWith("-addOneElement:")))
            {
                var vals = args.First(a => a.StartsWith("-addOneElement:")).Split(':');
                LoadOneEntryFromFile(vals[1].ToLong());
            }

            if (args.Any(a => a == "-updateDatabase"))
            {
                UpdateExistingEntries(config["OutputDataFolder"]);
            }

            if (args.Any(a => a == "-updateDatabaseFast"))
            {
                UpdateExistingEntriesFast(config["OutputDataFolder"]);
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

            if (args.Any(a => a == "-findServerBounds"))
            {
                using var db = new PraxisContext();
                db.SetServerBounds(long.Parse(config["UseOneRelationID"]));
            }

            if (args.Any(a => a.StartsWith("-drawOneImage:")))
            {
                DrawOneImage(args.First(a => a.StartsWith("-drawOneImage:")).Split(":")[1]);
            }

            if (args.Any(a => a.StartsWith("-processCoastlines:")))
            {
                //NOTE: this is intended to read through the water polygon file. It'll probably run with the coastline linestring file, but that 
                //isn't going to draw what you want in PraxisMapper.
                string arg = args.First(a => a.StartsWith("-processCoastlines:"));
                string filename = arg.Substring(arg.IndexOf(":") + 1);
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
                MakeOfflineFilesCell8();
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

            //This is not currently finished or testing in the current setup. Will return in a future release.
            //if (args.Any(a => a.StartsWith("-populateEmptyArea:")))
            //{
            //    populateEmptyAreas(args.First(a => a.StartsWith("-populateEmptyArea:")).Split(":")[1]);
            //}
        }

        private static void SetEnvValues()
        {
            Log.WriteLog("Setting preferred NET environment variables for performance. A restart may be required for them to apply.");
            System.Environment.SetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT", "1", EnvironmentVariableTarget.Machine);
            System.Environment.SetEnvironmentVariable("COMPlus_TieredCompilation", "1", EnvironmentVariableTarget.Machine);
            System.Environment.SetEnvironmentVariable("DOTNET_TieredPGO", "1", EnvironmentVariableTarget.Machine);
            //TODO: perf test with these settings, as they may be necessary for ALL code to benefit from PGO. Saving for Release 9
            //System.Environment.SetEnvironmentVariable("DOTNET_ReadyToRun", "0", EnvironmentVariableTarget.Machine);
        }

        private static void PwdSpeedTest()
        {
            Log.WriteLog("Determining the correct value for Rounds on this computer for saving passwords...");
            System.Diagnostics.Stopwatch encryptTimer = new System.Diagnostics.Stopwatch();
            int rounds = 6;
            while (encryptTimer.ElapsedMilliseconds < 250)
            {
                rounds++;
                var results = BCrypt.Net.BCrypt.EnhancedHashPassword("anythingWillDo", rounds);
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

        private static void processPbfs()
        {
            List<string> filenames = System.IO.Directory.EnumerateFiles(config["PbfFolder"], "*.pbf").ToList();
            foreach (string filename in filenames)
            {
                Log.WriteLog("Loading " + filename + " at " + DateTime.Now);
                PbfReader r = new PbfReader();
                r.outputPath = config["OutputDataFolder"];
                r.styleSet = config["TagParserStyleSet"];
                r.processingMode = config["processingMode"]; // "normal" and "center" and "minimize" allowed
                r.saveToDB = config["UseGeomDataFiles"] == "False"; //optional, usually faster to load from files than going straight to the DB.
                r.onlyMatchedAreas = config["OnlyTaggedAreas"] == "True";
                r.reprocessFile = config["reprocessFiles"] == "True";

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

        private static void loadProcessedData()
        {
            Log.WriteLog("Starting load from processed files at " + DateTime.Now);
            System.Diagnostics.Stopwatch fullProcess = new System.Diagnostics.Stopwatch();
            fullProcess.Start();
            PraxisContext db = new PraxisContext();
            db.Database.SetCommandTimeout(Int32.MaxValue);
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            List<string> geomFilenames = Directory.EnumerateFiles(config["OutputDataFolder"], "*.geomData").ToList();
            List<string> tagsFilenames = Directory.EnumerateFiles(config["OutputDataFolder"], "*.tagsData").ToList();

            db.DropIndexes();

            if (config["KeepElementsInMemory"] == "true") //ignore DB, doing some one-off operation.
            {
                //Skip database work. Use an in-memory list for a temporary operation.
                foreach (var fileName in geomFilenames)
                {
                    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                    Log.WriteLog("Loading " + fileName + " to memory at " + DateTime.Now);
                    var entries = File.ReadAllLines(fileName);
                    foreach (var entry in entries)
                    {
                        DbTables.Place stored = GeometrySupport.ConvertSingleTsvPlace(entry);
                        memorySource.Add(stored);
                    }

                    Log.WriteLog("File loaded to memory in " + sw.Elapsed);
                    sw.Stop();
                }
                foreach (var fileName in tagsFilenames)
                {
                    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                    Log.WriteLog("Loading " + fileName + " to memory at " + DateTime.Now);
                    var entries = File.ReadAllLines(fileName);
                    foreach (var entry in entries)
                    {
                        PlaceTags stored = GeometrySupport.ConvertSingleTsvTag(entry);
                        var taggedGeo = memorySource.First(m => m.SourceItemType == stored.SourceItemType && m.SourceItemID == stored.SourceItemId);
                        //MemorySource will need to be a more efficient collection for searching if this is to be a major feature, but this functions.
                        taggedGeo.Tags.Add(stored);
                    }

                    Log.WriteLog("File applied to memory in " + sw.Elapsed);
                    sw.Stop();
                }
                return;
            }
            else if (config["UseGeomDataFiles"] == "True")
            {
                if (config["DbMode"] == "MariaDB")
                {
                    foreach (var fileName in geomFilenames)
                    {

                        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                        sw.Start();
                        var mariaPath = fileName.Replace("\\", "\\\\"); //TODO: this may need some cross-platform attention.
                        db.Database.ExecuteSqlRaw("LOAD DATA INFILE '" + mariaPath + "' IGNORE INTO TABLE Places fields terminated by '\t' lines terminated by '\r\n' (sourceItemID, sourceItemType, @elementGeometry, privacyId, DrawSizeHint) SET elementGeometry = ST_GeomFromText(@elementGeometry) ");
                        sw.Stop();
                        Log.WriteLog("Geometry loaded from " + fileName + " in " + sw.Elapsed);
                        Task.Run(() => File.Move(fileName, fileName + "done"));
                    }

                    foreach (var fileName in tagsFilenames)
                    {
                        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                        sw.Start();
                        var mariaPath = fileName.Replace("\\", "\\\\");
                        db.Database.ExecuteSqlRaw("LOAD DATA INFILE '" + mariaPath + "' IGNORE INTO TABLE PlaceTags fields terminated by '\t' lines terminated by '\r\n' (SourceItemId, SourceItemType, `key`, `value`)");
                        sw.Stop();
                        Log.WriteLog("Tags loaded from " + fileName + " in " + sw.Elapsed);
                        Task.Run(() => File.Move(fileName, fileName + "done"));
                    }
                }
                else if (config["DbMode"] == "SQLServer" || config["DbMode"] == "LocalDB") //UseGeomFiles == true
                {
                    foreach (var fileName in geomFilenames)
                    {
                        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                        sw.Start();
                        db.Database.ExecuteSqlRaw("CREATE TABLE #tempPlace ( id bigint, osmType int, geoText nvarchar(MAX), privacyID uniqueidentifier, hintsize decimal(30,17)); " +
                            "BULK INSERT #tempPlace FROM '" + fileName + "';" +
                            "INSERT INTO dbo.Places (SourceItemId, SourceItemType, ElementGeometry, PrivacyId, DrawSizeHint) SELECT id, osmType, geography::STGeomFromText(geoText, 4326), privacyID, hintSize FROM #tempPlace; " +
                            "DROP TABLE #tempPlace;"
                            );
                        sw.Stop();
                        Log.WriteLog("Geometry loaded from " + fileName + " in " + sw.Elapsed);
                        File.Move(fileName, fileName + "done");
                    }

                    foreach (var fileName in tagsFilenames)
                    {
                        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                        sw.Start();
                        db.Database.ExecuteSqlRaw("CREATE TABLE #tempTags ( id bigint, osmType int, tagKey nvarchar(MAX), tagValue nvarchar(MAX)); " +
                            "BULK INSERT #tempTags FROM '" + fileName + "';" +
                            "INSERT INTO dbo.PlaceTags (SourceItemId, SourceItemType, [Key], [Value]) SELECT id, osmType, tagKey, tagValue FROM #tempTags; " +
                            "DROP TABLE #tempTags;"
                            );
                        sw.Stop();
                        Log.WriteLog("Tags loaded from " + fileName + " in " + sw.Elapsed);
                        File.Move(fileName, fileName + "done");
                    }
                }
            }
            else //Main path, write directly to DB.
            {
                var options = new ParallelOptions();
                if (singleThread)
                    options.MaxDegreeOfParallelism = 1;

                foreach (var fileName in geomFilenames)
                {
                    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                    sw.Start();
                    db = new PraxisContext();
                    db.Database.SetCommandTimeout(Int32.MaxValue);
                    db.ChangeTracker.AutoDetectChangesEnabled = false;
                    var lines = File.ReadAllLines(fileName); //Might be faster to use streams and dodge the memory allocation?
                    var newPlaces = new List<DbTables.Place>(lines.Length);
                    foreach (var line in lines)
                    {
                        db.Places.Add(GeometrySupport.ConvertSingleTsvPlace(line));
                    }
                    db.SaveChanges();
                    sw.Stop();
                    Log.WriteLog("Geometry loaded from " + fileName + " in " + sw.Elapsed);
                    File.Move(fileName, fileName + "done");
                }
                foreach (var fileName in tagsFilenames)
                {
                    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                    sw.Start();
                    db = new PraxisContext();
                    db.Database.SetCommandTimeout(Int32.MaxValue);
                    db.ChangeTracker.AutoDetectChangesEnabled = false;
                    var lines = File.ReadAllLines(fileName);
                    foreach (var line in lines)
                    {
                        db.PlaceTags.Add(GeometrySupport.ConvertSingleTsvTag(line));
                    }
                    db.SaveChanges();
                    sw.Stop();
                    Log.WriteLog("Tags loaded from " + fileName + " in " + sw.Elapsed);
                    File.Move(fileName, fileName + "done");
                }//);
            }

            fullProcess.Stop();
            Log.WriteLog("Files processed in " + fullProcess.Elapsed);
            fullProcess.Restart();
            db.RecreateIndexes();
            fullProcess.Stop();
            Log.WriteLog("Indexes generated in " + fullProcess.Elapsed);
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
            File.WriteAllBytes(config["OutputDataFolder"] + code + ".png", MapTileSupport.DrawPlusCode(code, paintOps));
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
                geometryFactory = NtsGeometryServices.Instance.CreateGeometryFactory(new PrecisionModel(1000000), 4326); //SRID matches 10-character Plus code values.  Precision model means round all points to 7 decimal places to not exceed float's useful range.
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
            //NOTE: this requires the WGS84 version of the polygons. the Mercator version is UTM, not the Mercator you saw in school.
            Log.WriteLog("Reading water polygon data from " + shapePath);
            Stopwatch sw = Stopwatch.StartNew();
            string fileBaseName = config["OutputDataFolder"] + "coastlines";
            EGIS.ShapeFileLib.ShapeFile sf = new EGIS.ShapeFileLib.ShapeFile(shapePath);
            var recordCount = sf.RecordCount;
            using (StreamWriter geoSW = new StreamWriter(fileBaseName + ".geomData"))
            using (StreamWriter tagSW = new StreamWriter(fileBaseName + ".tagsData"))
                for (int i = 0; i < recordCount; i++)
                {
                    var shapeData = sf.GetShapeDataD(i);
                    var poly = Converters.ShapefileRecordToPolygon(shapeData);
                    geoSW.WriteLine((100000000000 + i) + "\t2\t" + poly.AsText() + "\t" + Guid.NewGuid() + "\t999999\r\n"); //DrawSizeHint is big so these always appear.
                    tagSW.WriteLine((100000000000 + i) + "\t2\tnatural\twater");
                    tagSW.WriteLine((100000000000 + i) + "\t2\tbgwater\tpraxismapper");
                }
            sw.Stop();
            Log.WriteLog("Water polygon data converted to PraxisMapper geomdata files in " + sw.Elapsed);
        }

        public static void UpdateExistingEntries(string path)
        {
            List<string> filenames = Directory.EnumerateFiles(path, "*.geomData").ToList();
            ParallelOptions po = new ParallelOptions();
            //if (singleThread)
            po.MaxDegreeOfParallelism = 1;
            Parallel.ForEach(filenames, po, (filename) =>
            {
                try
                {
                    using var db = new PraxisContext();
                    Log.WriteLog("Loading " + filename);
                    var entries = GeometrySupport.ReadPlaceFilesToMemory(filename); //tagsData file loaded automatically here.
                    Log.WriteLog(entries.Count + " entries to check in database for " + filename);
                    var updated = db.UpdateExistingEntries(entries);
                    File.Move(filename, filename + "Done");
                    Log.WriteLog(filename + " completed at " + DateTime.Now + ", updated " + updated + " rows");
                }
                catch (Exception ex)
                {
                    Log.WriteLog("Error multithreading: " + ex.Message + ex.StackTrace, Log.VerbosityLevels.Errors);
                }
            });
        }

        public static void UpdateExistingEntriesFast(string path)
        {
            List<string> filenames = Directory.EnumerateFiles(path, "*.geomData").ToList();
            ParallelOptions po = new ParallelOptions();
            if (singleThread)
                po.MaxDegreeOfParallelism = 1;
            else
                po.MaxDegreeOfParallelism = 4;
            Parallel.ForEach(filenames, po, (filename) =>
            {
                try
                {
                    using var db = new PraxisContext();
                    Log.WriteLog("Loading " + filename);
                    var entries = GeometrySupport.ReadPlaceFilesToMemory(filename); //tagsData file loaded automatically here.
                    Log.WriteLog(entries.Count + " entries to check in database for " + filename);
                    var updated = db.UpdateExistingEntriesFast(entries);
                    File.Move(filename, filename + "Done");
                    Log.WriteLog(filename + " completed at " + DateTime.Now + ", updated " + updated + " rows");
                }
                catch (Exception ex)
                {
                    Log.WriteLog("Error multithreading: " + ex.Message + ex.StackTrace, Log.VerbosityLevels.Errors);
                }
            });

            using var db = new PraxisContext();
            db.ExpireAllMapTiles();
            db.ExpireAllSlippyMapTiles();
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
                    //    File.WriteAllText(config["OutputDataFolder"] + cell2 + cell4 + ".json", JsonSerializer.Serialize(terrainDict));
                    //    terrainDict[cell2].TryRemove(cell4, out var xx);
                    //    Log.WriteLog("Made file for " + cell2 + cell4 + " at " + DateTime.Now);
                    //}
                }
                if (terrainDict[cell2].IsEmpty)
                    terrainDict[cell2].TryRemove(cell2, out _);
                else
                {
                    File.WriteAllText(config["OutputDataFolder"] + cell2 + ".json", JsonSerializer.Serialize(terrainDict));
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

        public static void ReduceServerSize() //Extremely similar to the PBFReader reprocessing, but online instead of against files.
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
                    place.ElementGeometry = NetTopologySuite.Precision.GeometryPrecisionReducer.Reduce(NetTopologySuite.Simplify.TopologyPreservingSimplifier.Simplify(place.ElementGeometry, ConstantValues.resolutionCell10), PrecisionModel.FloatingSingle.Value);
                    var match = TagParser.GetStyleEntry(place, "mapTiles");
                    var name = TagParser.GetName(place);
                    place.Tags.Clear();
                    if (!string.IsNullOrEmpty(name))
                        place.Tags.Add(new PlaceTags() { Key = "name", Value = name });
                    place.Tags.Add(new PlaceTags() { Key = "styleset", Value = match.Name });
                }
                Log.WriteLog("Saving " + groupSize + " changes");
                db.SaveChanges();
                groupsDone++;
            }
        }

        public static void RecalcDrawSizeHints()
        {
            //TODO: write something that lets me quick and easy batch commands on the entities.
            using var db = new PraxisContext();
            var groupsDone = 0;
            var groupSize = 10000;
            bool keepGoing = true;
            long lastEntry = 0; //This appears to be faster than Skip. Should confirm.
            while (keepGoing)
            {
                //var places = db.Places.Include(p => p.Tags).Skip(groupsDone * groupSize).Take(groupSize).ToList();
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

        //I should really call it Condensed data if I'm taking it back online.
        public static void LoadOfflineDataToDb(string path)
        {
            Log.WriteLog("Loading offline-data JSONs to online DB.");
            List<string> filenames = Directory.EnumerateFiles(path, "*.json").ToList();
            using var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var settings = db.ServerSettings.First();
            var nextId = settings.NextAutoGeneratedAreaId;

            foreach (var file in filenames)
            {
                Log.WriteLog("Processing " + file);
                Stopwatch sw = Stopwatch.StartNew();
                var savedData = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, string>>>>>(File.ReadAllText(file));
                //A big stack of dictionaries with ints. Pop those into Cell8 sized geometries.
                var index = savedData["index"].Keys.First().Split("|");
                var styleKeys = index.ToDictionary(k => k.Split(',')[1], v => v.Split(",")[0]);
                savedData.Remove("index");

                Dictionary<string, List<Geometry>> futureGeoms = new Dictionary<string, List<Geometry>>();
                foreach (var s in styleKeys)
                    futureGeoms.Add(s.Key, new List<Geometry>());

                int totalAreas = 0;
                GeoArea geoArea = null;
                foreach (var Cell2 in savedData)
                    foreach (var Cell4 in Cell2.Value)
                        foreach (var Cell6 in Cell4.Value)
                            foreach (var Cell8 in Cell6.Value)
                            {
                                var allStyles = Cell8.Value.Split("|");
                                geoArea = OpenLocationCode.DecodeValid(Cell2.Key + Cell4.Key + Cell6.Key + Cell8.Key);
                                foreach (var s in allStyles)
                                    futureGeoms[s].Add(geoArea.ToPolygon());
                                totalAreas++;
                            }

                foreach (var geom in futureGeoms)
                {
                    if (geom.Value == null || geom.Value.Count == 0)
                        continue;

                    var results = NetTopologySuite.Operation.Union.CascadedPolygonUnion.Union(geom.Value).Simplify(0.00000001);

                    //Write all polygons to database
                    MultiPolygon mp = results as MultiPolygon;
                    if (mp == null || mp.Geometries.Length == 0)
                        continue;

                    foreach (var p in mp.Geometries)
                    {
                        var place = new DbTables.Place();
                        place.ElementGeometry = p;
                        place.SourceItemID = nextId++;
                        place.SourceItemType = 2;
                        place.StyleName = styleKeys[geom.Key];
                        place.PrivacyId = Guid.NewGuid();
                        place.Tags = new List<PlaceTags>() {
                            new PlaceTags() { Place = place, Key = "suggestedmini", Value = styleKeys[geom.Key]},
                            new PlaceTags() { Place = place, Key = "generated", Value = "PraxisMapper" }
                        };
                        place.DrawSizeHint = GeometrySupport.CalculateDrawSizeHint(place);
                        db.Places.Add(place);
                    }
                    sw.Stop();
                    Log.WriteLog("Processed " + totalAreas + " Cell8s into " + mp.Geometries.Length + " entries in " + sw.Elapsed + ".");
                }
                settings.NextAutoGeneratedAreaId = nextId;
                db.Entry(settings).State = EntityState.Modified;
                db.SaveChanges();
                File.Move(file, file + "-done");
            }
            Log.WriteLog("Loading offline data complete");
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
            place.ElementGeometry = NetTopologySuite.Precision.GeometryPrecisionReducer.Reduce(NetTopologySuite.Simplify.TopologyPreservingSimplifier.Simplify(place.ElementGeometry, ConstantValues.resolutionCell10), PrecisionModel.FloatingSingle.Value);
            var match = TagParser.GetStyleEntry(place, "mapTiles");
            var name = TagParser.GetName(place);
            place.Tags.Clear();
            if (!string.IsNullOrEmpty(name))
                place.Tags.Add(new PlaceTags() { Key = "name", Value = name });
            place.Tags.Add(new PlaceTags() { Key = "styleset", Value = match.Name });
        };

        public static void ExtractSubMap(long parentPlace, long parentPlaceType)
        {
            //Given the id/type of a parent Place (presumably an AdminBound, but not necessarily), extract all places that interesect to a text file(s).
            var db = new PraxisContext();

            var parent = db.Places.Where(p => p.SourceItemID == parentPlace && p.SourceItemType == parentPlaceType).FirstOrDefault();
            if (parent == null)
            {
                Log.WriteLog("Place " + parentPlace + " not found in database, not extracting map.");
                return; 
            }

            int skip = 0;
            int take = 1000; //test value.
            bool keepGoing = true;

            StringBuilder geoFileOut = new StringBuilder();
            StringBuilder tagFileOut = new StringBuilder();

            while (keepGoing)
            {
                geoFileOut.Clear();
                tagFileOut.Clear();
                var allPlaces = db.Places.Where(p => p.ElementGeometry.Intersects(parent.ElementGeometry)).Skip(skip).Take(take).ToList();
                if (allPlaces.Count < take)
                    keepGoing = false;

                skip += take;

                foreach (var p in allPlaces)
                {
                    //split place data into appropriate components. Leaving NewGuid here so sub-servers dont have the same data as parent server.
                    geoFileOut.Append(p.SourceItemID).Append('\t').Append(p.SourceItemType).Append('\t').Append(p.ElementGeometry.AsText()).Append('\t').Append(Guid.NewGuid()).Append('\t').Append(p.DrawSizeHint).Append("\r\n");
                    foreach (var t in p.Tags)
                        tagFileOut.Append(p.SourceItemID).Append('\t').Append(p.SourceItemType).Append('\t').Append(t.Key).Append('\t').Append(t.Value.Replace("\r", "").Replace("\n", "")).Append("\r\n");
                }

                File.AppendAllText(config["OutputDataFolder"] + parentPlace + "-submap.geomData", geoFileOut.ToString());
                File.AppendAllText(config["OutputDataFolder"] + parentPlace + "-submap.tagsData", geoFileOut.ToString());
                Log.WriteLog(skip + " items written to file total so far.");
            }
            Log.WriteLog(parentPlace + "-submap files written to OutputData folder at " + DateTime.Now);
        }
    }
}
