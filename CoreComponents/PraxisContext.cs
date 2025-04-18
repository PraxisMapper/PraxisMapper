﻿using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using static PraxisCore.DbTables;

namespace PraxisCore
{
    /// <summary>
    /// A self-contained database connector for everything PraxisMapper can do in its database.
    /// </summary>
    public class PraxisContext : DbContext //Not sealed: I want games to be able to extend this and add tables/logic.
    {
        public DbSet<PlayerData> PlayerData { get; set; }
        public DbSet<PerformanceInfo> PerformanceInfo { get; set; }
        public DbSet<MapTile> MapTiles { get; set; }
        public DbSet<SlippyMapTile> SlippyMapTiles { get; set; }
        public DbSet<ErrorLog> ErrorLogs { get; set; }
        public DbSet<ServerSetting> ServerSettings { get; set; }
        public DbSet<DbTables.Place> Places { get; set; }
        public DbSet<StyleEntry> StyleEntries { get; set; }
        public DbSet<StyleMatchRule> StyleMatchRules { get; set; }
        public DbSet<StylePaint> StylePaints { get; set; }
        public DbSet<PlaceTags> PlaceTags { get; set; }
        public DbSet<PlaceData> PlaceData { get; set; }
        public DbSet<AreaData> AreaData { get; set; }
        public DbSet<GlobalData> GlobalData { get; set; }
        public DbSet<StyleBitmap> StyleBitmaps { get; set; }
        public DbSet<AntiCheatEntry> AntiCheatEntries { get; set; }
        public DbSet<AuthenticationData> AuthenticationData { get; set; }
        public DbSet<OfflinePlace> OfflinePlaces { get; set; }

        public static string connectionString = "Data Source=localhost\\SQLDEV;UID=PraxisService;PWD=lamepassword;Initial Catalog=Praxis;"; //Needs a default value.
        public static string serverMode = "SQLServer";

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (serverMode == "SQLServer" || serverMode == "LocalDB")
                optionsBuilder.EnableThreadSafetyChecks(false).UseSqlServer(connectionString, x => x.UseNetTopologySuite());
            else if (serverMode == "MariaDB")
            {
                optionsBuilder.EnableThreadSafetyChecks(false).UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), x => x.UseNetTopologySuite().EnableRetryOnFailure());
            }
            else if (serverMode == "PostgreSQL") //A lot of mapping stuff defaults to PostgreSQL, so I should evaluate it here. It does seem to take some specific setup steps, versus MariaDB
            {
                optionsBuilder.EnableThreadSafetyChecks(false).UseNpgsql("Host=localhost;Database=praxis;Username=postgres;Password=asdf", o => o.UseNetTopologySuite());
            }

            //optionsBuilder.UseMemoryCache(mc);//I think this improves performance at the cost of RAM usage. Needs additional testing.
        }

        protected override void OnModelCreating(ModelBuilder model)
        {
            //Create indexes here.
            model.Entity<PlayerData>().HasIndex(p => p.accountId);
            model.Entity<PlayerData>().HasIndex(p => p.DataKey);
            model.Entity<PlayerData>().HasIndex(p => p.Expiration);

            model.Entity<DbTables.Place>().HasIndex(m => m.DrawSizeHint); //Enables server-side sorting on biggest-to-smallest draw order.
            model.Entity<DbTables.Place>().HasIndex(m => m.SourceItemID);
            model.Entity<DbTables.Place>().HasIndex(m => m.SourceItemType);
            model.Entity<DbTables.Place>().HasIndex(m => m.PrivacyId);
            model.Entity<DbTables.Place>().HasMany(m => m.Tags).WithOne(m => m.Place).HasForeignKey(m => (new { m.SourceItemId, m.SourceItemType })).HasPrincipalKey(m => (new { m.SourceItemID, m.SourceItemType }));

            model.Entity<MapTile>().HasIndex(m => m.PlusCode);
            model.Entity<MapTile>().Property(m => m.PlusCode).HasMaxLength(12);
            model.Entity<MapTile>().HasIndex(m => m.StyleSet);

            model.Entity<SlippyMapTile>().HasIndex(m => m.Values);
            model.Entity<SlippyMapTile>().HasIndex(m => m.StyleSet);

            model.Entity<PlaceTags>().HasIndex(m => m.Key);
            model.Entity<PlaceTags>().HasOne(m => m.Place).WithMany(m => m.Tags).HasForeignKey(m => new { m.SourceItemId, m.SourceItemType }).HasPrincipalKey(m => new { m.SourceItemID, m.SourceItemType });

            model.Entity<PlaceData>().HasIndex(m => m.DataKey);
            model.Entity<PlaceData>().HasIndex(m => m.DataValue); 
            model.Entity<PlaceData>().HasIndex(m => m.PlaceId);
            model.Entity<PlaceData>().HasIndex(m => m.Expiration);
            //TODO: add this index below to the drop/create functions for indexes.
            model.Entity<PlaceData>().HasIndex(["PlaceId", "DataKey"]); //MariaDB needs this to be able to use both indexes at once. Required for creating offline data in a timely manner.

            model.Entity<AreaData>().HasIndex(m => m.DataKey);
            model.Entity<AreaData>().HasIndex(m => m.PlusCode);
            //model.Entity<AreaData>().HasIndex(m => m.AreaCovered); //This breaks on SQL Server if you attempt to auto-create this, must be done manually.
            model.Entity<AreaData>().HasIndex(m => m.Expiration);

            model.Entity<AntiCheatEntry>().HasIndex(m => m.filename);

            model.Entity<AuthenticationData>().HasIndex(m => m.accountId);

            model.Entity<OfflinePlace>().HasIndex(m => m.DrawSizeHint); //Enables server-side sorting on biggest-to-smallest draw order.



            if (serverMode == "PostgreSQL")
            {
                model.HasPostgresExtension("postgis");
            }
        }

        //indexes that EFCore can't create correctly automatically.
        public static string MapTileIndex = "CREATE SPATIAL INDEX MapTileSpatialIndex ON MapTiles(areaCovered)";
        public static string SlippyMapTileIndex = "CREATE SPATIAL INDEX SlippyMapTileSpatialIndex ON SlippyMapTiles(areaCovered)";
        public static string PlacesIndex = "CREATE SPATIAL INDEX PlacesIndex ON Places(elementGeometry)";
        public static string AreaDataSpatialIndex = "CREATE SPATIAL INDEX areaDataSpatialIndex ON AreaData(AreaCovered)";
        public static string OfflinePlacesIndex = "CREATE SPATIAL INDEX OfflinePlacesIndex ON OfflinePlaces(elementGeometry)";

        //Text to recreate these indexes if I drop them for database loading.
        public static string drawSizeHintIndexMaria = "CREATE OR REPLACE INDEX IX_Places_DrawSizeHint on Places(DrawSizeHint)";
        public static string offlineDrawSizeHintIndexMaria = "CREATE OR REPLACE INDEX IX_OfflinePlaces_DrawSizeHint on OfflinePlaces(DrawSizeHint)";
        public static string privacyIdIndexMaria = "CREATE OR REPLACE INDEX IX_Places_privacyId on Places(privacyId)";
        public static string sourceItemIdIndexMaria = "CREATE OR REPLACE INDEX IX_Places_sourceItemID on Places(sourceItemID)";
        public static string sourceItemTypeIndexMaria = "CREATE OR REPLACE INDEX IX_Places_sourceItemType on Places(sourceItemType)";
        public static string tagKeyIndexMaria = "CREATE OR REPLACE INDEX IX_PlaceTags_Key on PlaceTags(`Key`)";
        public static string tagSourceIndexMaria = "CREATE OR REPLACE INDEX IX_PlaceTags_SourceItemId_SourceItemType on PlaceTags(SourceItemId, SourceItemType)";

        public static string drawSizeHintIndexSQL = "CREATE INDEX IX_Places_DrawSizeHint on Places(DrawSizeHint)";
        public static string offlineDrawSizeHintIndexSQL = "CREATE INDEX IX_OfflinePlaces_DrawSizeHint on OfflinePlaces(DrawSizeHint)";
        public static string privacyIdIndexSQL = "CREATE INDEX IX_Places_privacyId on Places(privacyId)";
        public static string sourceItemIdIndexSQL = "CREATE INDEX IX_Places_sourceItemID on Places(sourceItemID)";
        public static string sourceItemTypeIndexSQL = "CREATE INDEX IX_Places_sourceItemType on Places(sourceItemType)";
        public static string tagKeyIndexSQL = "CREATE INDEX IX_PlaceTags_Key on PlaceTags([Key])";
        public static string tagSourceIndexSQL = "CREATE INDEX IX_PlaceTags_SourceItemId_SourceItemType on PlaceTags(sourceItemId, sourceItemType)";

        //PostgreSQL uses its own CREATE INDEX syntax
        public static string MapTileIndexPG = "CREATE INDEX maptiles_geom_idx ON public.\"MapTiles\" USING GIST(\"areaCovered\")";
        public static string SlippyMapTileIndexPG = "CREATE INDEX slippmayptiles_geom_idx ON public.\"SlippyMapTiles\" USING GIST(\"areaCovered\")";
        public static string StoredElementsIndexPG = "CREATE INDEX place_geom_idx ON public.\"Places\" USING GIST(\"elementGeometry\")";

        //Adding these as helper values for large use cases. When inserting large amounts of data, it's probably worth removing indexes for the insert and re-adding them later.
        //(On a North-America file insert, this keeps insert speeds at about 2-3 seconds per block, whereas it creeps up consistently while indexes are updated per block.
        //Though, I also see better results there droping the single-column indexes as well, which would need re-created manually since those one are automatic.
        public static string DropMapTileIndex = "DROP INDEX IF EXISTS MapTileSpatialIndex on MapTiles";
        public static string DropSlippyMapTileIndex = "DROP INDEX IF EXISTS SlippyMapTileSpatialIndex on SlippyMapTiles";
        public static string DropPlacesSpatialIndex = "DROP INDEX IF EXISTS PlacesIndex on Places";
        public static string DropOfflinePlacesSpatialIndex = "DROP INDEX IF EXISTS OfflinePlacesIndex on OfflinePlaces";
        public static string DropAreaDataCoveredIndex = "DROP INDEX IF EXISTS areaDataSpatialIndex on AreaData";
        public static string DropPlacesDrawSizeHintIndex = "DROP INDEX IF EXISTS IX_Places_DrawSizeHint on Places";
        public static string DropOfflinePlacesDrawSizeHintIndex = "DROP INDEX IF EXISTS IX_OfflinePlaces_DrawSizeHint on OfflinePlaces";
        public static string DropPLacesPrivacyIdIndex = "DROP INDEX IF EXISTS IX_Places_privacyId on Places";
        public static string DropPlacesSourceItemIdIndex = "DROP INDEX IF EXISTS IX_Places_sourceItemID on Places";
        public static string DropPlacesSourceItemTypeIndex = "DROP INDEX IF EXISTS IX_Places_sourceItemType on Places";
        public static string DropTagKeyIndex = "DROP INDEX IF EXISTS IX_PlaceTags_Key on PlaceTags";
        public static string DropTagSourceIndex = "DROP INDEX IF EXISTS IX_PlaceTags_SourceItemId_SourceItemType on PlaceTags";

        //This doesn't appear to be any faster. The query isn't the slow part. Keeping this code as a reference for how to precompile queries.
        //public static Func<PraxisContext, Geometry, IEnumerable<MapData>> compiledIntersectQuery =
        //  EF.CompileQuery((PraxisContext context, Geometry place) => context.MapData.Where(md => md.place.Intersects(place)));

        public void MakePraxisDB()
        {
            if (serverMode == "LocalDB")
            {
                //check if already exists
                Process createdb = new Process();
                createdb.StartInfo.FileName = "SqlLocalDB.exe";
                createdb.StartInfo.Arguments = "info";
                createdb.StartInfo.RedirectStandardOutput = true;

                try
                {
                    createdb.Start();
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("The system cannot find the file specified") && Environment.OSVersion.Platform == PlatformID.Win32NT)
                    {
                        Log.WriteLog("LocalDB not found, installing.");
                        HttpClient hc = new HttpClient();
                        var installer = hc.GetByteArrayAsync("https://github.com/PraxisMapper/PraxisMapper/blob/master/SupportFiles/SQLLOCALDB.MSI?raw=true").Result;
                        System.IO.File.WriteAllBytes("SQLLOCALDB.MSI", installer);
                        createdb = new Process();
                        createdb.StartInfo.FileName = "msiexec";
                        createdb.StartInfo.Arguments = "/i SqlLocalDB.msi /qn IACCEPTSQLLOCALDBLICENSETERMS=YES";
                        createdb.Start();
                        createdb.WaitForExit();
                        Log.WriteLog("LocalDB installed");

                        // now let's try again.
                        createdb.StartInfo.Arguments = "info";
                        createdb.Start();
                    }
                    else
                    {
                        Log.WriteLog("LocalDB not found or couldn't start. Error message: " + ex.Message + ex.StackTrace);
                        return;
                    }
                }

                string line = createdb.StandardOutput.ReadLine();
                while (line != null)
                {
                    if (line.StartsWith("Praxis"))
                    {
                        createdb.Close();
                        return;
                    }
                    line = createdb.StandardOutput.ReadLine();
                }

                Log.WriteLog("Creating new LocalDB Praxis.");
                createdb = new Process();
                createdb.StartInfo.FileName = "SqlLocalDB.exe";
                createdb.StartInfo.Arguments = "create \"Praxis\" -s"; //create and start the new DB
                createdb.StartInfo.RedirectStandardOutput = true;
                createdb.StartInfo.RedirectStandardError = true;
                createdb.Start();
                createdb.WaitForExit();
            }

            if (!Database.EnsureCreated()) //all the automatic stuff EF does for us
                return;

            //Not automatic entries executed below:
            if (serverMode == "PostgreSQL")
            {
                Database.ExecuteSqlRaw(MapTileIndexPG);
                Database.ExecuteSqlRaw(SlippyMapTileIndexPG);
                Database.ExecuteSqlRaw(StoredElementsIndexPG);
            }
            else //SQL Server and MariaDB share the same syntax here
            {
                //NOTE: SQL/LocalDB and MariaDB no longer can use this same text. SQL wants a bounding box defined, Mariadb doesnt.
                //(or this might be a consequence of changing from geography to geometry)
                Database.ExecuteSqlRaw(MapTileIndex);
                Database.ExecuteSqlRaw(SlippyMapTileIndex);
                Database.ExecuteSqlRaw(PlacesIndex);
                Database.ExecuteSqlRaw(AreaDataSpatialIndex);
                Database.ExecuteSqlRaw(OfflinePlacesIndex);
            }

            if (serverMode == "SQLServer" || serverMode == "LocalDB")
            {

            }

            if (serverMode == "MariaDB")
            {
                Database.ExecuteSqlRaw("SET collation_server = 'utf8mb4_unicode_ci'; SET character_set_server = 'utf8mb4'"); //MariaDB defaults to latin2_swedish, we need Unicode.
            }

            InsertDefaultServerConfig();
            InsertDefaultStyle();
        }

        public void InsertDefaultServerConfig()
        {
            ServerSettings.Add(new ServerSetting() { NorthBound = 90, SouthBound = -90, EastBound = 180, WestBound = -180, MessageOfTheDay = "" });
            SaveChanges();
        }

        public void InsertDefaultStyle()
        {
            if (serverMode == "SQLServer" || serverMode == "LocalDB")
            {
                Database.BeginTransaction();
                Database.ExecuteSqlRaw("SET IDENTITY_INSERT StylePaints ON;");
            }
            if (Singletons.defaultStyleEntries.Count == 0)
            {
                //We have been called before TagParser was initialized.
                TagParser.Initialize(true, null);
            }
            StyleEntries.AddRange(Singletons.defaultStyleEntries);
            SaveChanges();
            if (serverMode == "SQLServer" || serverMode == "LocalDB")
            {
                Database.ExecuteSqlRaw("SET IDENTITY_INSERT StylePaints OFF;");
                Database.CommitTransaction();
            }

            foreach (var file in System.IO.Directory.EnumerateFiles("MapPatterns"))
            {
                StyleBitmaps.Add(new StyleBitmap()
                {
                    Filename = System.IO.Path.GetFileName(file),
                    Data = System.IO.File.ReadAllBytes(file)
                });
            }
            SaveChanges();
        }

        public void RecreateIndexes()
        {
            //Only run this after running DropIndexes, since these should all exist on DB creation.
            Log.WriteLog("Recreating indexes...");
            Database.SetCommandTimeout(60000);

            //PostgreSQL will make automatic spatial indexes
            if (serverMode == "PostgreSQL")
            {
                //db.Database.ExecuteSqlRaw(GeneratedMapDataIndexPG);
                Database.ExecuteSqlRaw(MapTileIndexPG);
                Database.ExecuteSqlRaw(SlippyMapTileIndexPG);
                Database.ExecuteSqlRaw(StoredElementsIndexPG);
            }
            else if (serverMode == "MariaDB")
            {
                var sw = Stopwatch.StartNew();
                //db.Database.ExecuteSqlRaw(GeneratedMapDataIndex);
                Database.ExecuteSqlRaw(MapTileIndex);
                Log.WriteLog("MapTiles indexed in " + sw.Elapsed);
                sw.Restart();
                Database.ExecuteSqlRaw(SlippyMapTileIndex);
                Log.WriteLog("SlippyMapTiles indexed in " + sw.Elapsed);
                sw.Restart();
                Database.ExecuteSqlRaw(PlacesIndex);
                Log.WriteLog("Places geometry indexed in " + sw.Elapsed);
                sw.Restart();
                Database.ExecuteSqlRaw(OfflinePlacesIndex);
                Log.WriteLog("Offline Places geometry indexed in " + sw.Elapsed);

                //now also add the automatic ones we took out in DropIndexes.
                sw.Restart();
                Database.ExecuteSqlRaw(drawSizeHintIndexMaria);
                Database.ExecuteSqlRaw(offlineDrawSizeHintIndexMaria);
                Database.ExecuteSqlRaw(sourceItemIdIndexMaria);
                Database.ExecuteSqlRaw(sourceItemTypeIndexMaria);
                Database.ExecuteSqlRaw(privacyIdIndexMaria);
                Log.WriteLog("Places other columns indexed in " + sw.Elapsed );
                sw.Restart();
                Database.ExecuteSqlRaw(tagKeyIndexMaria);
                Log.WriteLog("PlaceTags indexed in " + sw.Elapsed);
                sw.Restart();
                Database.ExecuteSqlRaw(AreaDataSpatialIndex);
                Log.WriteLog("AreaData indexed in " + sw.Elapsed);
            }
            else if (serverMode == "SQLServer" || serverMode == "LocalDB")
            {
                var sw = Stopwatch.StartNew();
                //db.Database.ExecuteSqlRaw(GeneratedMapDataIndex);
                Database.ExecuteSqlRaw(MapTileIndex);
                Log.WriteLog("MapTiles indexed in " + sw.Elapsed);
                sw.Restart();
                Database.ExecuteSqlRaw(SlippyMapTileIndex);
                Log.WriteLog("SlippyMapTiles indexed in " + sw.Elapsed);
                sw.Restart();
                Database.ExecuteSqlRaw(PlacesIndex);
                Log.WriteLog("Places geometry indexed in " + sw.Elapsed);
                sw.Restart();
                Database.ExecuteSqlRaw(OfflinePlacesIndex);
                Log.WriteLog("Offline Places geometry indexed in " + sw.Elapsed);
                sw.Restart();

                //now also add the automatic ones we took out in DropIndexes.
                Database.ExecuteSqlRaw(drawSizeHintIndexSQL);
                Database.ExecuteSqlRaw(offlineDrawSizeHintIndexSQL);
                Database.ExecuteSqlRaw(sourceItemIdIndexSQL);
                Database.ExecuteSqlRaw(sourceItemTypeIndexSQL);
                Database.ExecuteSqlRaw(privacyIdIndexSQL);
                Log.WriteLog("Places other columns indexed in " + sw.Elapsed);
                sw.Restart();
                Database.ExecuteSqlRaw(tagKeyIndexSQL);
                //Database.ExecuteSqlRaw(tagSourceIndexSQL);
                Log.WriteLog("PlaceTags indexed in " + sw.Elapsed);
                sw.Restart();
                Database.ExecuteSqlRaw(AreaDataSpatialIndex);
                Log.WriteLog("AreaData indexed in " + sw.Elapsed);
            }
        }

        public void DropIndexes()
        {
            Log.WriteLog("Dropping indexes.....");
            Database.ExecuteSqlRaw(DropMapTileIndex);
            Database.ExecuteSqlRaw(DropPlacesSpatialIndex);
            Database.ExecuteSqlRaw(DropOfflinePlacesSpatialIndex);
            Database.ExecuteSqlRaw(DropSlippyMapTileIndex);
            Database.ExecuteSqlRaw(DropAreaDataCoveredIndex);
            Database.ExecuteSqlRaw(DropPlacesDrawSizeHintIndex);
            Database.ExecuteSqlRaw(DropOfflinePlacesDrawSizeHintIndex);
            Database.ExecuteSqlRaw(DropPLacesPrivacyIdIndex);
            Database.ExecuteSqlRaw(DropPlacesSourceItemTypeIndex);
            Database.ExecuteSqlRaw(DropPlacesSourceItemIdIndex);
            Database.ExecuteSqlRaw(DropTagKeyIndex);
            //Not dropping the sourceItemId index because of a requirement from a foreign key.
            Log.WriteLog("Indexes dropped.");
        }

        /// <summary>
        /// Force gameplay maptiles to expire and be redrawn on next access. Can be limited to a specific style set or run on all tiles.
        /// </summary>
        /// <param name="g">the area to expire intersecting maptiles with</param>
        /// <param name="styleSet">which set of maptiles to expire. All tiles if this is an empty string</param>
        public int ExpireMapTiles(Geometry g, string styleSet = "")
        {

            //Security check: styleSet is provided from external sources, validate that input before dropping it into SQL.
            if (styleSet != "" && !TagParser.allStyleGroups.ContainsKey(styleSet))
                return 0;

            string SQL = "";
            if (serverMode == "MariaDB")
            {
                SQL = "UPDATE MapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE (styleSet= '" + styleSet + "' OR '" + styleSet + "' = '') AND ST_INTERSECTS(areaCovered, ST_GEOMFROMTEXT('" + g.Envelope.AsText() + "', 4326))";
            }
            else if (serverMode == "SQLServer" || serverMode == "LocalDB")
            {
                SQL = "UPDATE MapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE (styleSet= '" + styleSet + "' OR '" + styleSet + "' = '') AND areaCovered.STIntersects(geography::STGeomFromText('" + g.Envelope.AsText() + "', 4326).MakeValid()) = 1";
            }
            return Database.ExecuteSqlRaw(SQL);
        }

        public int ExpireAllMapTiles()
        {
            string SQL = "UPDATE MapTiles SET ExpireOn = CURRENT_TIMESTAMP";
            return Database.ExecuteSqlRaw(SQL);
        }

        /// <summary>
        /// Force gameplay maptiles to expire and be redrawn on next access. Can be limited to a specific style set or run on all tiles.
        /// </summary>
        /// <param name="elementId">the privacyID of a Place to expire intersecting tiles for.</param>
        /// <param name="styleSet">which set of maptiles to expire. All tiles if this is an empty string</param>
        public int ExpireMapTiles(Guid elementId, string styleSet = "")
        {
            string SQL = "";
            if (serverMode == "MariaDB")
            {
                SQL = "UPDATE MapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE (styleSet= '" + styleSet + "' OR '" + styleSet + "' = '') AND ST_INTERSECTS(areaCovered, (SELECT elementGeometry FROM Places WHERE privacyId = '" + elementId.ToString() + "'))";
            }
            else if (serverMode == "SQLServer" || serverMode == "LocalDB")
            {
                SQL = "UPDATE MapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE (styleSet= '" + styleSet + "' OR '" + styleSet + "' = '') AND areaCovered.STIntersects((SELECT elementGeometry FROM Places WHERE privacyID = '" + elementId.ToString() + "')) = 1";
            }
            return Database.ExecuteSqlRaw(SQL);
        }

        /// <summary>
        /// Force SlippyMap tiles to expire and be redrawn on next access. Can be limited to a specific style set or run on all tiles.
        /// </summary>
        /// <param name="g">the area to expire intersecting maptiles with</param>
        /// <param name="styleSet">which set of SlippyMap tiles to expire. All tiles if this is an empty string</param>
        public void ExpireSlippyMapTiles(Geometry g, string styleSet = "")
        {
            string SQL = "";
            if (serverMode == "MariaDB")
            {
                SQL = "UPDATE SlippyMapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE (styleSet= '" + styleSet + "' OR '" + styleSet + "' = '') AND ST_INTERSECTS(areaCovered, ST_GEOMFROMTEXT('" + g.Envelope.AsText() + "', 4326))";
            }
            else if (serverMode == "SQLServer" || serverMode == "LocalDB")
            {
                SQL = "UPDATE SlippyMapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE (styleSet= '" + styleSet + "' OR '" + styleSet + "' = '') AND areaCovered.STIntersects(geography::STGeomFromText('" + g.Envelope.AsText() + "', 4326).MakeValid()) = 1";
            }
            Database.ExecuteSqlRaw(SQL);
        }

        /// <summary>
        /// Force SlippyMap tiles to expire and be redrawn on next access. Can be limited to a specific style set or run on all tiles.
        /// </summary>
        /// <param name="elementId">the privacyID of a Place to expire intersecting tiles for.</param>
        /// <param name="styleSet">which set of maptiles to expire. All tiles if this is an empty string</param>
        public void ExpireSlippyMapTiles(Guid elementId, string styleSet = "")
        {
            string SQL = "";
            if (serverMode == "MariaDB")
            {
                SQL = "UPDATE SlippyMapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE (styleSet = '" + styleSet + "' OR '" + styleSet + "' = '') AND ST_INTERSECTS(areaCovered, (SELECT elementGeometry FROM Places WHERE privacyId = '" + elementId.ToString() + "'))";
            }
            else if (serverMode == "SQLServer" || serverMode == "LocalDB")
            {
                SQL = "UPDATE SlippyMapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE (styleSet= '" + styleSet + "' OR '" + styleSet + "' = '') AND areaCovered.STIntersects((SELECT elementGeometry FROM Places WHERE privacyId = '" + elementId.ToString() + "')) = 1";
            }
            Database.ExecuteSqlRaw(SQL);
        }
        public void ExpireAllSlippyMapTiles()
        {
            string SQL = "UPDATE SlippyMapTiles SET ExpireOn = CURRENT_TIMESTAMP";
            Database.ExecuteSqlRaw(SQL);
        }

        public GeoArea SetServerBounds(Geometry relation)
        {
            var envelop = relation.EnvelopeInternal;
            var results = new GeoArea(envelop.MinY, envelop.MinX, envelop.MaxY, envelop.MaxX);

            var settings = ServerSettings.FirstOrDefault();
            if (settings == null) //Shouldn't happen, but an error in the right spot and re-running the process can cause this.
            {
                settings = new ServerSetting();
                ServerSettings.Add(settings);
            }
            settings.NorthBound = results.NorthLatitude;
            settings.SouthBound = results.SouthLatitude;
            settings.EastBound = results.EastLongitude;
            settings.WestBound = results.WestLongitude;
            SaveChanges();
            Log.WriteLog("Server bounds set to (" + settings.SouthBound + "," + settings.WestBound + "), (" + settings.NorthBound + "," + settings.EastBound + ")");
            return results;
        }

        public GeoArea SetServerBounds(long singlePlace)
        {
            //This is an important command if you don't want to track data outside of your initial area.
            GeoArea results = null;
            if (singlePlace != 0)
            {
                Log.WriteLog("Setting server bounds to envelope of " + singlePlace);
                var area = Places.FirstOrDefault(e => e.SourceItemID == singlePlace);
                if (area == null)
                {
                    //singleArea probably wasn't a valid ID.
                    throw new Exception("Could not find Place ID " + singlePlace + "in database");
                }
                return SetServerBounds(area.ElementGeometry);
            }
            else
            {
                Log.WriteLog("Auto-detecting server boundaries....");
                results = Place.DetectServerBounds(ConstantValues.resolutionCell8);
            }

            var settings = ServerSettings.FirstOrDefault();
            if (settings == null) //Shouldn't happen, but an error in the right spot and re-running the process can cause this.
            {
                settings = new ServerSetting();
                ServerSettings.Add(settings);
            }
            settings.NorthBound = results.NorthLatitude;
            settings.SouthBound = results.SouthLatitude;
            settings.EastBound = results.EastLongitude;
            settings.WestBound = results.WestLongitude;
            SaveChanges();
            Log.WriteLog("Server bounds set to (" + settings.SouthBound + "," + settings.WestBound + "), (" + settings.NorthBound + "," + settings.EastBound + ")");
            return results;
        }

        public void ResetStyles()
        {
            Log.WriteLog("Replacing current styles with default ones");
            var styles = Singletons.defaultStyleEntries.Select(t => t.StyleSet).Distinct().ToList();

            var toRemove = StyleEntries.Include(t => t.PaintOperations).Include(t => t.StyleMatchRules).Where(t => styles.Contains(t.StyleSet)).ToList();
            var toRemovePaints = toRemove.SelectMany(t => t.PaintOperations).ToList();
            var toRemoveImages = StyleBitmaps.ToList();
            var toRemoveRules = toRemove.SelectMany(t => t.StyleMatchRules).ToList();
            StylePaints.RemoveRange(toRemovePaints);
            SaveChanges();
            StyleMatchRules.RemoveRange(toRemoveRules);
            SaveChanges();
            StyleEntries.RemoveRange(toRemove);
            SaveChanges();
            StyleBitmaps.RemoveRange(toRemoveImages);
            SaveChanges();

            InsertDefaultStyle();
            Log.WriteLog("Styles restored to PraxisMapper defaults");
        }
    }
}