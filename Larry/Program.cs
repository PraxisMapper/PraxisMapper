using PraxisCore;
using PraxisCore.Support;
using Google.OpenLocationCode;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using static PraxisCore.ConstantValues;
using static PraxisCore.DbTables;
using static PraxisCore.Place;
using static PraxisCore.Singletons;
using static PraxisCore.StandaloneDbTables;
using PraxisCore.PbfReader;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using CryptSharp;
using System.Reflection;


//TODO: look into using Span<T> instead of lists? This might be worth looking at performance differences. (and/or Memory<T>, which might be a parent for Spans)
//TODO: Ponder using https://download.bbbike.org/osm/ as a data source to get a custom extract of an area (for when users want a local-focused app, probably via a wizard GUI)
//OR could use an additional input for filterbox.

namespace Larry
{
    class Program
    {
        static IConfigurationRoot config;
        static List<StoredOsmElement> memorySource;
        static IMapTiles MapTiles;
        
        static void Main(string[] args)
        {
            Console.WriteLine("PGO is " + System.Environment.GetEnvironmentVariable("DOTNET_TieredPGO"));
            var builder = new ConfigurationBuilder()
            .AddJsonFile("Larry.config.json");
            config = builder.Build();

            //var memMon = new MemoryMonitor();
            ApplyConfigValues();

            if (true)
            {
                var asm = Assembly.LoadFrom(@"PraxisMapTilesSkiaSharp.dll");
                MapTiles = (IMapTiles)Activator.CreateInstance(asm.GetType("PraxisCore.MapTiles"));
            }   
            else
            {
                var asm2 = Assembly.LoadFrom(@"PraxisMapTilesImageSharp.dll"); //works in debug. path isn't gonna work in publish.
                MapTiles = (IMapTiles)Activator.CreateInstance(asm2.GetType("PraxisCore.MapTiles"));
            }


            Log.WriteLog("Larry started at " + DateTime.Now);

            if (args.Count() == 0)
            {
                Console.WriteLine("You must pass an arguement to this application");
                //TODO: list valid commands or point at the docs file
                return;
            }

            if (config["KeepElementsInMemory"] == "True")
                memorySource = new List<StoredOsmElement>(20000);

            //Sanity check some values.
            if (config["UseMariaDBInFile"] == "True" && config["DbMode"] != "MariaDB")
            {
                Console.WriteLine("You set a MariaDB-only option on and aren't using MariaDB! Fix the configs to use MariaDB or disable the InFile setting and run again.");
                return;
            }

            //If multiple args are supplied, run them in the order that make sense, not the order the args are supplied.
            if (args.Any(a => a == "-createDB")) //setup the destination database
            {
                createDb();
            }

            if (args.Any(a => a.StartsWith("-getPbf:")))
            {
                //Wants 3 pieces. Drops in placeholders if some are missing. Giving no parameters downloads Ohio.
                string arg = args.First(a => a.StartsWith("-getPbf:")).Replace("-getPbf:", "");
                var splitData = arg.Split('|'); //remember the first one will be empty.
                string level1 = splitData.Count() >= 4 ? splitData[3] : "north-america";
                string level2 = splitData.Count() >= 3 ? splitData[2] : "us";
                string level3 = splitData.Count() >= 2 ? splitData[1] : "ohio";

                DownloadPbfFile(level1, level2, level3, config["PbfFolder"]);
            }

            if (args.Any(a => a == "-resetPbf"))
            {
                FileCommands.ResetFiles(config["PbfFolder"]);
            }

            if (args.Any(a => a == "-resetJson"))
            {
                FileCommands.ResetFiles(config["JsonMapDataFolder"]);
            }

            if (args.Any(a => a == "-resetStyles"))
            {
                ResetStyles();
            }

            if (args.Any(a => a == "-processPbfs"))
            {
                processPbfs();
            }

            if (args.Any(a => a == "-loadProcessedData"))
            {
                loadProcessedData();
            }

            if (args.Any(a => a == "-makeServerDb"))
            {
                //This is the single command to get a server going, assuming you have done all the setup steps yourself beforehand and your config is correct.
                createDb();
                processPbfs();
                loadProcessedData();
                var bounds = DBCommands.FindServerBounds(long.Parse(config["UseOneRelationID"]));
                MapTileSupport.PregenMapTilesForArea(bounds);

                Log.WriteLog("Server setup complete.");
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

            //TODO: rework Update process to handle the mulitple data files that could be used.
            //if (args.Any(a => a == "-updateDatabase"))
            //{
            //DBCommands.UpdateExistingEntries(config["JsonMapDataFolder"]);

            if (args.Any(a => a.StartsWith("-createStandaloneRelation")))
            {
                //This makes a standalone DB for a specific relation passed in as a paramter. 
                int relationId = Int32.Parse(config["UseOneRelationID"]);
                CreateStandaloneDB(relationId, null, false, true); //How map tiles are handled is determined by the optional parameters
            }

            if (args.Any(a => a.StartsWith("-createStandaloneBox")))
            {
                //This makes a standalone DB for a specific area passed in as a paramter.
                //If you want to cover a region in a less-specific way, or the best available relation is much larger than you thought, this might be better.
                string[] bounds = args.First(a => a.StartsWith("-createStandaloneBox")).Split('|');
                GeoArea boundsArea = new GeoArea(bounds[1].ToDouble(), bounds[2].ToDouble(), bounds[3].ToDouble(), bounds[4].ToDouble());

                //in order, these go south/west/north/east.
                CreateStandaloneDB(0, boundsArea, false, true); //How map tiles are handled is determined by the optional parameters
            }

            if (args.Any(a => a.StartsWith("-createStandalonePoint")))
            {
                //This makes a standalone DB centered on a specific point, it will grab a Cell6's area around that point.
                string[] bounds = args.First(a => a.StartsWith("-createStandalonePoint")).Split('|');

                var resSplit = resolutionCell6 / 2;
                GeoArea boundsArea = new GeoArea(bounds[1].ToDouble() - resSplit, bounds[2].ToDouble() - resSplit, bounds[1].ToDouble() + resSplit, bounds[2].ToDouble() + resSplit);

                //in order, these go south/west/north/east.
                CreateStandaloneDB(0, boundsArea, false, true); //How map tiles are handled is determined by the optional parameters
            }

            if (args.Any(a => a == "-autoCreateMapTiles")) //better for letting the app decide which tiles to create than manually calling out Cell6 names.
            {
                var bounds = DBCommands.FindServerBounds(long.Parse(config["UseOneRelationID"]));
                MapTileSupport.PregenMapTilesForArea(bounds);
            }

            if (args.Any(a => a == "-findServerBounds"))
            {
                DBCommands.FindServerBounds(long.Parse(config["UseOneRelationID"]));
            }

            if (args.Any(a => a.StartsWith("-drawOneImage:")))
            {
                DrawOneImage(args.First(a => a.StartsWith("-drawOneImage:")).Split(":")[1]);
            }

            if (args.Any(a => a.StartsWith("-processCoastlines:")))
            {
                string filename = args.First(a => a.StartsWith("-processCoastlines:")).Split(":")[1];
                ReadCoastlineShapefile(filename);
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

            //This is not currently finished or testing in the current setup. Will return in a future release.
            //if (args.Any(a => a.StartsWith("-populateEmptyArea:")))
            //{
            //    populateEmptyAreas(args.First(a => a.StartsWith("-populateEmptyArea:")).Split(":")[1]);
            //}
        }

        private static void SetEnvValues()
        {
            Log.WriteLog("Setting preferred NET 6 environment variables for performance. A restart may be required for them to apply.");
            System.Environment.SetEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT", "1", EnvironmentVariableTarget.Machine);
            System.Environment.SetEnvironmentVariable("COMPlus_TieredCompilation", "1", EnvironmentVariableTarget.Machine);
            System.Environment.SetEnvironmentVariable("DOTNET_TieredPGO", "1", EnvironmentVariableTarget.Machine);
        }

        private static void PwdSpeedTest()
        {
            Log.WriteLog("Determining the correct value for Rounds on this computer for saving passwords...");
            System.Diagnostics.Stopwatch encryptTimer = new System.Diagnostics.Stopwatch();
            int rounds = 6;
            while (encryptTimer.ElapsedMilliseconds < 250)
            {
                rounds++;
                var options = new CrypterOptions() {
                        { CrypterOption.Rounds, rounds}
                    };
                encryptTimer.Restart();
                BlowfishCrypter crypter = new BlowfishCrypter();
                var salt = crypter.GenerateSalt(options);
                var results = crypter.Crypt("anythingWillDo", salt);
                encryptTimer.Stop();
                Console.WriteLine("Time with Rounds:" + rounds + ": " + encryptTimer.ElapsedMilliseconds + "ms");

            }
            Log.WriteLog("Suggestion: Set the PasswordRounds configuration variable to " + rounds + " in PraxisMapper's appsettings.json file");
        }

        private static void createDb()
        {
            Console.WriteLine("Creating database with current database settings.");
            var db = new PraxisContext();
            db.MakePraxisDB();
        }

        private static void processPbfs()
        {
            List<string> filenames = System.IO.Directory.EnumerateFiles(config["PbfFolder"], "*.pbf").ToList();
            foreach (string filename in filenames)
            {
                Log.WriteLog("Loading " + filename + " at " + DateTime.Now);
                PbfReader r = new PbfReader();
                r.outputPath = config["JsonMapDataFolder"];
                r.styleSet = config["TagParserStyleSet"];
                r.processingMode = config["processingMode"]; // "normal" and "center" allowed
                r.saveToInfile = config["UseMariaDBInFile"] == "True";
                r.saveToJson = !r.saveToInfile;
                r.saveToDB = false; //config["UseMariaDBInFile"] != "True";
                r.onlyMatchedAreas = config["OnlyTaggedAreas"] == "True";
                r.readJsonFile = config["reprocessJson"] == "True";
                r.ProcessFile(filename, long.Parse(config["UseOneRelationID"]));
                File.Move(filename, filename + "done");
            }
        }

        private static void loadProcessedData()
        {
            Log.WriteLog("Starting load from processed files at " + DateTime.Now);
            PraxisContext db = null;
            if (config["KeepElementsInMemory"] != "True")
            {
                db = new PraxisContext();
                db.Database.SetCommandTimeout(Int32.MaxValue);
                db.ChangeTracker.AutoDetectChangesEnabled = false;
            }

            if (config["UseMariaDBInFile"] == "True")
            {
                List<string> filenames = System.IO.Directory.EnumerateFiles(config["JsonMapDataFolder"], "*.geomInfile").ToList();
                foreach (var jsonFileName in filenames)
                {
                    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                    sw.Start();
                    var mariaPath = jsonFileName.Replace("\\", "\\\\");
                    db.Database.ExecuteSqlRaw("LOAD DATA INFILE '" + mariaPath + "' IGNORE INTO TABLE StoredOsmElements fields terminated by '\t' lines terminated by '\r\n' (name, sourceItemID, sourceItemType, @elementGeometry, AreaSize, privacyId) SET elementGeometry = ST_GeomFromText(@elementGeometry) ");
                    sw.Stop();
                    Console.WriteLine("Geometry loaded from " + jsonFileName + " in " + sw.Elapsed);
                    System.IO.File.Move(jsonFileName, jsonFileName + "done");
                }

                filenames = System.IO.Directory.EnumerateFiles(config["JsonMapDataFolder"], "*.tagsInfile").ToList();
                foreach (var jsonFileName in filenames)
                {
                    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                    sw.Start();
                    var mariaPath = jsonFileName.Replace("\\", "\\\\");
                    db.Database.ExecuteSqlRaw("LOAD DATA INFILE '" + mariaPath + "' IGNORE INTO TABLE ElementTags fields terminated by '\t' lines terminated by '\r\n' (SourceItemId, SourceItemType, `key`, `value`)");
                    sw.Stop();
                    Console.WriteLine("Tags loaded from " + jsonFileName + " in " + sw.Elapsed);
                    System.IO.File.Move(jsonFileName, jsonFileName + "done");
                }
            }
            else if (config["KeepElementsInMemory"] == "True")
            {
                //Read stuff to RAM to use for testing 
                List<string> filenames = System.IO.Directory.EnumerateFiles(config["JsonMapDataFolder"], "*.json").ToList();
                foreach (var jsonFileName in filenames)
                {
                    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                    Log.WriteLog("Loading " + jsonFileName + " to database at " + DateTime.Now);
                    var fr = File.OpenRead(jsonFileName);
                    var sr = new StreamReader(fr);
                    List<StoredOsmElement> pendingData = new List<StoredOsmElement>(10050);
                    sw.Start();
                    while (!sr.EndOfStream)
                    {
                        //NOTE: the slowest part of getting a server going now is inserting into the DB. 
                        string entry = sr.ReadLine();
                        StoredOsmElement stored = GeometrySupport.ConvertSingleJsonStoredElement(entry);
                        memorySource.Add(stored);
                    }

                    Log.WriteLog("Files loaded to memory in " + sw.Elapsed);
                    sw.Stop();
                    sr.Close(); sr.Dispose();
                    fr.Close(); fr.Dispose();
                }
            }
            else //typical Json files
            {
                List<string> filenames = System.IO.Directory.EnumerateFiles(config["JsonMapDataFolder"], "*.json").ToList();
                long entryCounter = 0;
                foreach (var jsonFileName in filenames)
                {
                    System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                    Log.WriteLog("Loading " + jsonFileName + " to database at " + DateTime.Now);
                    var fr = File.OpenRead(jsonFileName);
                    var sr = new StreamReader(fr);
                    List<StoredOsmElement> pendingData = new List<StoredOsmElement>(10050);
                    sw.Start();
                    while (!sr.EndOfStream)
                    {
                        //NOTE: the slowest part of getting a server going now is inserting into the DB. 
                        string entry = sr.ReadLine();
                        StoredOsmElement stored = GeometrySupport.ConvertSingleJsonStoredElement(entry);
                        pendingData.Add(stored);

                        entryCounter++;
                        if (entryCounter >= 10000)
                        {
                            var splitLists = pendingData.SplitListToMultiple(4);
                            List<Task> lt = new List<Task>(4);
                            foreach (var list in splitLists)
                                lt.Add(Task.Run(() => { var db = new PraxisContext(); db.ChangeTracker.AutoDetectChangesEnabled = false; db.StoredOsmElements.AddRange(list); db.SaveChanges(); }));
                            Task.WaitAll(lt.ToArray());
                            Log.WriteLog("10,000 elements saved in " + sw.Elapsed);
                            sw.Restart();

                            pendingData.Clear();
                            entryCounter = 0;
                        }
                    }

                    var splitLists2 = pendingData.SplitListToMultiple(4);
                    List<Task> lt2 = new List<Task>(4);
                    foreach (var list in splitLists2)
                        lt2.Add(Task.Run(() => { var db = new PraxisContext(); db.ChangeTracker.AutoDetectChangesEnabled = false; db.StoredOsmElements.AddRange(list); db.SaveChanges(); }));
                    Task.WaitAll(lt2.ToArray());
                    Log.WriteLog("Final save done in " + sw.Elapsed);
                    sw.Stop();
                    sr.Close(); sr.Dispose();
                    fr.Close(); fr.Dispose();
                    File.Move(jsonFileName, jsonFileName + "done");
                }
            }
        }
        private static void autoCreateMapTiles()
        {
            //Remember: this shouldn't draw GeneratedMapTile areas, nor should this create them.
            //Tiles should be redrawn when those get made, if they get made.
            //This should also over-write existing map tiles if present, in case the data's been updated since last run.
            bool skip = true; //This skips over 128,000 tiles in about a minute. Decent.

            //Search for all areas that needs a map tile created.
            List<string> Cell2s = new List<string>();

            //Cell2 detection loop: 22-CV (162). All others are 22-XX (400 sub-entries). 
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

        private static void ResetStyles()
        {
            Log.WriteLog("Replacing current styles with default ones");
            var db = new PraxisContext();
            var styles = Singletons.defaultTagParserEntries.Select(t => t.styleSet).Distinct().ToList();

            var toRemove = db.TagParserEntries.Include(t => t.paintOperations).Where(t => styles.Contains(t.styleSet)).ToList();
            var toRemovePaints = toRemove.SelectMany(t => t.paintOperations).ToList();
            db.TagParserPaints.RemoveRange(toRemovePaints);
            db.SaveChanges();
            db.TagParserEntries.RemoveRange(toRemove);
            db.SaveChanges();

            db.TagParserEntries.AddRange(Singletons.defaultTagParserEntries);
            db.SaveChanges();
            Log.WriteLog("Styles restored to PraxisMapper defaults");
        }

        private static void DrawOneImage(string code)
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            TagParser.ApplyTags(memorySource, "mapTiles");
            ImageStats istats = new ImageStats(OpenLocationCode.DecodeValid(code), 1024, 1024);
            var paintOps = MapTileSupport.GetPaintOpsForStoredElements(memorySource, "mapTiles", istats);
            System.IO.File.WriteAllBytes(config["JsonMapDataFolder"] + code + ".png", MapTileSupport.DrawPlusCode(code, paintOps, "mapTiles"));
            sw.Stop();
            Log.WriteLog("image drawn from memory in " + sw.Elapsed);
        }

        private static void DrawPosterOfServer()
        {
            var db = new PraxisContext();
            var bounds = db.ServerSettings.First();

            var geoArea = new GeoArea(bounds.SouthBound, bounds.WestBound, bounds.NorthBound, bounds.EastBound);
            //do the math to scale image.
            //the smaller side is set to 24", the larger size scales up proportionally up to a max of 36"
            //if the longer side is > 36", scale both down by the difference?

            //36x24 is target poster size, at 300 dpi, our image size will allow for an inch of margin on both axes.
            var dpi = 300;
            var maxXSide = 35 * dpi;
            var maxYSide = 23 * dpi;
            var xSize = 0;
            var ySize = 0;

            var heightScale = geoArea.LatitudeHeight / geoArea.LongitudeWidth; //Y pixels per X pixel
            if (heightScale > 1) // Y axis is longer than X axis
            {
                heightScale = geoArea.LongitudeWidth / geoArea.LatitudeHeight;
                maxXSide = 23 * dpi;
                maxYSide = 35 * dpi;
                ySize = maxYSide;
                xSize = (int)(maxXSide * heightScale);
            }
            else
            {
                xSize = maxXSide;
                ySize = (int)(maxYSide * heightScale);
            }

            Log.WriteLog("Loading all places from DB");
            var places = GetPlaces(geoArea);
            var iStats = new ImageStats(geoArea, xSize, ySize);
            Log.WriteLog("Generating paint operations");
            var paintOps = MapTileSupport.GetPaintOpsForStoredElements(places, "mapTiles", iStats);
            Log.WriteLog("Drawing image");
            var image = MapTiles.DrawAreaAtSize(iStats, paintOps); //, TagParser.GetStyleBgColor("mapTiles"));

            File.WriteAllBytes("ServerPoster.png", image);
            Log.WriteLog("Image saved to disk");
        }

        private static void populateEmptyAreas(string cell6)
        {
            var db = new PraxisContext();
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

        private static void ApplyConfigValues()
        {
            PraxisContext.connectionString = config["DbConnectionString"];
            PraxisContext.serverMode = config["DbMode"];

            if (config["UseHighAccuracy"] != "True")
            {
                factory = NtsGeometryServices.Instance.CreateGeometryFactory(new PrecisionModel(1000000), 4326); //SRID matches 10-character Plus code values.  Precision model means round all points to 7 decimal places to not exceed float's useful range.
                SimplifyAreas = true; //rounds off points that are within a Cell10's distance of each other. Makes fancy architecture and highly detailed paths less pretty on map tiles, but works for gameplay data.
            }

            int logLevel = int.Parse(config["LogLevel"]);
            switch (logLevel)
            {
                case 0:
                    Log.Verbosity = Log.VerbosityLevels.Off;
                    break;
                case 1:
                    Log.Verbosity = Log.VerbosityLevels.Normal;
                    break;
                case 2:
                    Log.Verbosity = Log.VerbosityLevels.High;
                    break;
            }

            TagParser.Initialize(config["ForceTagParserDefaults"] == "True", MapTiles); //Do this after the DB values are parsed.
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
                System.Threading.Tasks.Parallel.For(0, 20, (pos2) =>
                {
                    string cellToCheck = parentCell + OpenLocationCode.CodeAlphabet[pos1].ToString() + OpenLocationCode.CodeAlphabet[pos2].ToString();
                    var area = new OpenLocationCode(cellToCheck).Decode();
                    ImageStats info = new ImageStats(area, 160, 200); //values for Cell8 sized area with 2xCell11 resolution.
                    if (cellToCheck.Length == 8) //We don't want to do the DoPlacesExist check here, since we'll want empty tiles for empty areas at this l
                    {
                        var places = GetPlaces(area, cell6Data); //These are cloned in GetPlaces, so we aren't intersecting areas twice and breaking drawing. //, false, false, 0
                        var tileData = MapTiles.DrawAreaAtSize(info, places);
                        tilesGenerated.Add(new MapTile() { CreatedOn = DateTime.Now, styleSet = "mapTiles", tileData = tileData, resolutionScale = 11, PlusCode = cellToCheck });
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
                var fullArea = mainDb.StoredOsmElements.FirstOrDefault(m => m.sourceItemID == relationID && m.sourceItemType == 3);
                if (fullArea == null)
                    return;

                buffered = Converters.GeometryToGeoArea(fullArea.elementGeometry);
                //This should also be able to take a bounding box in addition in the future.
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

            var placeInfo = PraxisCore.Standalone.Standalone.GetPlaceInfo(basePlaces);
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
            var indexCell6 = PraxisCore.Standalone.Standalone.IndexAreasPerCell6(buffered, basePlaces);
            var indexes = indexCell6.SelectMany(i => i.Value.Select(v => new PlaceIndex() { PlusCode = i.Key, placeInfoId = placeDictionary[v.sourceItemID].id })).ToList();
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

                GeoArea thisPath = Converters.GeometryToGeoArea(trail.elementGeometry);
                List<StoredOsmElement> oneEntry = new List<StoredOsmElement>();
                oneEntry.Add(trail);

                var overlapped = AreaTypeInfo.SearchArea(ref thisPath, ref oneEntry);
                if (overlapped.Count() > 0)
                {
                    tdSmalls.TryAdd(trail.name, new TerrainDataSmall() { Name = trail.name, areaType = trail.GameElementName });
                }
                foreach (var o in overlapped)
                {
                    var ti = new TerrainInfo();
                    ti.PlusCode = o.Key.Substring(removableLetters, 10 - removableLetters);
                    ti.TerrainDataSmall = new List<TerrainDataSmall>();
                    ti.TerrainDataSmall.Add(tdSmalls[trail.name]);
                    sqliteDb.TerrainInfo.Add(ti);
                }
                sqliteDb.SaveChanges();
            }

            foreach (var r in toRemove.Distinct())
                sqliteDb.PlaceInfo2s.Remove(r);
            sqliteDb.SaveChanges();
            Log.WriteLog("Trails processed at " + DateTime.Now);

            //make scavenger hunts
            var sh = PraxisCore.Standalone.Standalone.GetScavengerHunts(allPlaces);
            sqliteDb.ScavengerHunts.AddRange(sh);
            sqliteDb.SaveChanges();
            Log.WriteLog("Auto-created scavenger hunt entries at " + DateTime.Now);

            var swCorner = new OpenLocationCode(intersectCheck.EnvelopeInternal.MinY, intersectCheck.EnvelopeInternal.MinX);
            var neCorner = new OpenLocationCode(intersectCheck.EnvelopeInternal.MaxY, intersectCheck.EnvelopeInternal.MaxX);
            //insert default entries for a new player.
            sqliteDb.PlayerStats.Add(new PlayerStats() { timePlayed = 0, distanceWalked = 0, score = 0 });
            sqliteDb.Bounds.Add(new Bounds() { EastBound = neCorner.Decode().EastLongitude, NorthBound = neCorner.Decode().NorthLatitude, SouthBound = swCorner.Decode().SouthLatitude, WestBound = swCorner.Decode().WestLongitude, commonCodeLetters = commonStart, BestIdleCompletionTime = 0, LastPlayedOn = 0, StartedCurrentIdleRun = 0 });
            sqliteDb.IdleStats.Add(new IdleStats() { emptySpacePerSecond = 0, emptySpaceTotal = 0, graveyardSpacePerSecond = 0, graveyardSpaceTotal = 0, natureReserveSpacePerSecond = 0, natureReserveSpaceTotal = 0, parkSpacePerSecond = 0, parkSpaceTotal = 0, touristSpacePerSecond = 0, touristSpaceTotal = 0, trailSpacePerSecond = 0, trailSpaceTotal = 0 });
            sqliteDb.SaveChanges();

            //now we have the list of places we need to be concerned with. 
            System.IO.Directory.CreateDirectory(relationID + "Tiles");
            PraxisCore.Standalone.Standalone.DrawMapTilesStandalone(relationID, buffered, allPlaces, saveToFolder);
            sqliteDb.SaveChanges();
            Log.WriteLog("Maptiles drawn at " + DateTime.Now);

            //Copy the files as necessary to their correct location.
            if (saveToFolder)
                Directory.Move(relationID + "Tiles", config["Solar2dExportFolder"] + "Tiles");

            File.Copy(relationID + ".sqlite", config["Solar2dExportFolder"] + "database.sqlite");

            Log.WriteLog("Standalone gameplay DB done.");
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

        public static void ReadCoastlineShapefile(string shapePath)
        {
            StreamWriter sw = new StreamWriter(config["JsonMapDataFolder"] + "coastlines.json");
            EGIS.ShapeFileLib.ShapeFile sf = new EGIS.ShapeFileLib.ShapeFile(shapePath);
            var recordCount = sf.RecordCount;
            var tagString = "natural:coastline";
            for (int i = 0; i < recordCount; i++)
            {
                var shapeData = sf.GetShapeDataD(i);
                var poly = Converters.ShapefileRecordToPolygon(shapeData);
                //Write this to a json file.
                var jsonData = new StoredOsmElementForJson(i, "coastlinePoly", i, 2, poly.ToString(), tagString, false, false, false);
                var jsonString = System.Text.Json.JsonSerializer.Serialize(jsonData, typeof(StoredOsmElementForJson));
                sw.WriteLine(jsonString);
            }
            sw.Close();
            sw.Dispose();
        }
    }
}
