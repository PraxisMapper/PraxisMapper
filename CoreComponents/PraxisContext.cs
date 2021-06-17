using Microsoft.EntityFrameworkCore;
using System;
using static CoreComponents.DbTables;

namespace CoreComponents
{
    public class PraxisContext : DbContext
    {
        public DbSet<PlayerData> PlayerData { get; set; }
        public DbSet<PerformanceInfo> PerformanceInfo { get; set; }
        public DbSet<MapTile> MapTiles { get; set; }
        public DbSet<SlippyMapTile> SlippyMapTiles { get; set; }
        public DbSet<Faction> Factions { get; set; }
        public DbSet<AreaControlTeam> AreaControlTeams { get; set; } //This is TeamClaims, rename this
        public DbSet<PaintTownConfig> PaintTownConfigs { get; set; }
        public DbSet<PaintTownEntry> PaintTownEntries { get; set; }
        public DbSet<PaintTownScoreRecord> PaintTownScoreRecords { get; set; }
        public DbSet<ErrorLog> ErrorLogs { get; set; }
        public DbSet<ServerSetting> ServerSettings { get; set; }
        public DbSet<TileTracking> TileTrackings { get; set; }
        public DbSet<ZztGame> ZztGames { get; set; }
        public DbSet<GamesBeaten> GamesBeaten { get; set; }
        public DbSet<StoredOsmElement> StoredOsmElements { get; set; }
        public DbSet<TagParserEntry> TagParserEntries { get; set; }
        public DbSet<TagParserMatchRule> TagParserMatchRules { get; set; }

        public DbSet<ElementTags> ElementTags { get; set; } //This table is exposed so I can search it directly faster.

        public DbSet<ScavengerHunt> scavengerHunts { get; set; }
        public DbSet<ScavengerHuntEntry> scavengerHuntEntries { get; set; }


        //IConfiguration Config;
        public static string connectionString = "Data Source=localhost\\SQLDEV;UID=GpsExploreService;PWD=lamepassword;Initial Catalog=Praxis;"; //Needs a default value.
        public static string serverMode = "SQLServer";

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (serverMode == "SQLServer")
                optionsBuilder.UseSqlServer(connectionString, x => x.UseNetTopologySuite());
            else if (serverMode == "MariaDB")
            {
                optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), x => x.UseNetTopologySuite().EnableRetryOnFailure());
            }
            else if (serverMode == "PostgreSQL") //A lot of mapping stuff defaults to PostgreSQL, so I should evaluate it here. It does seem to take some specific setup steps, versus MariaDB
            {
                optionsBuilder.UseNpgsql("Host=localhost;Database=praxis;Username=postgres;Password=asdf", o => o.UseNetTopologySuite());
            }

            //optionsBuilder.UseMemoryCache(mc);//I think this improves performance at the cost of RAM usage. Needs additional testing.
        }

        protected override void OnModelCreating(ModelBuilder model)
        {
            //Create indexes here.
            model.Entity<PlayerData>().HasIndex(p => p.deviceID); //for updating data

            model.Entity<StoredOsmElement>().HasIndex(m => m.AreaSize); //Enables server-side sorting on biggest-to-smallest draw order.
            model.Entity<StoredOsmElement>().HasIndex(m => m.sourceItemID);
            model.Entity<StoredOsmElement>().HasIndex(m => m.sourceItemType);

            model.Entity<MapTile>().HasIndex(m => m.PlusCode);
            model.Entity<MapTile>().Property(m => m.PlusCode).HasMaxLength(12);
            model.Entity<MapTile>().HasIndex(m => m.mode);

            model.Entity<SlippyMapTile>().HasIndex(m => m.Values);
            model.Entity<SlippyMapTile>().HasIndex(m => m.mode);

            model.Entity<AreaControlTeam>().HasIndex(m => m.StoredElementId);
            model.Entity<AreaControlTeam>().HasIndex(m => m.FactionId);

            model.Entity<PaintTownEntry>().HasIndex(m => m.FactionId);
            model.Entity<PaintTownEntry>().HasIndex(m => m.PaintTownConfigId);
            model.Entity<PaintTownEntry>().HasIndex(m => m.Cell8); //index for looking up current tiles.
            model.Entity<PaintTownEntry>().HasIndex(m => m.Cell10); //index for claiming
            //No index on the claimedAt column, since the Cell8Recent call should use the Cell8 index, and then scanning over a max of 400 entries shouldn't be terrible. Add that here if this assumption is wrong.

            model.Entity<PaintTownScoreRecord>().HasIndex(m => m.PaintTownConfigId);
            model.Entity<PaintTownScoreRecord>().HasIndex(m => m.WinningFactionID);

            model.Entity<ElementTags>().HasIndex(m => m.Key);

            if (serverMode == "PostgreSQL")
            {
                model.HasPostgresExtension("postgis");
            }
        }

        //A trigger to ensure all data inserted is valid by SQL Server rules.
        public static string MapDataValidTriggerMSSQL = "CREATE TRIGGER dbo.MakeValid ON dbo.MapData AFTER INSERT AS BEGIN UPDATE dbo.MapData SET place = place.MakeValid() WHERE MapDataId in (SELECT MapDataId from inserted) END";
        public static string GeneratedMapDataValidTriggerMSSQL = "CREATE TRIGGER dbo.GenereatedMapDataMakeValid ON dbo.GeneratedMapData AFTER INSERT AS BEGIN UPDATE dbo.GeneratedMapData SET place = place.MakeValid() WHERE GeneratedMapDataId in (SELECT GeneratedMapDataId from inserted) END";

        //An index that I don't think EFCore can create correctly automatically.
        public static string GeneratedMapDataIndex = "CREATE SPATIAL INDEX GeneratedMapDataSpatialIndex ON GeneratedMapData(place)";
        public static string MapTileIndex = "CREATE SPATIAL INDEX MapTileSpatialIndex ON MapTiles(areaCovered)";
        public static string SlippyMapTileIndex = "CREATE SPATIAL INDEX SlippyMapTileSpatialIndex ON SlippyMapTiles(areaCovered)";
        public static string StoredElementsIndex = "CREATE SPATIAL INDEX StoredOsmElementsIndex ON StoredOsmElements(elementGeometry)";

        //PostgreSQL uses its own CREATE INDEX syntax
        public static string GeneratedMapDataIndexPG = "CREATE INDEX generatedmapdata_geom_idx ON public.\"GeneratedMapData\" USING GIST(place)";
        public static string MapTileIndexPG = "CREATE INDEX maptiles_geom_idx ON public.\"MapTiles\" USING GIST(\"areaCovered\")";
        public static string SlippyMapTileIndexPG = "CREATE INDEX slippmayptiles_geom_idx ON public.\"SlippyMapTiles\" USING GIST(\"areaCovered\")";
        public static string StoredElementsIndexPG = "CREATE INDEX storedOsmElements_geom_idx ON public.\"StoredOsmElements\" USING GIST(\"elementGeometry\")";

        //This doesn't appear to be any faster. The query isn't the slow part. Keeping this code as a reference for how to precompile queries.
        //public static Func<PraxisContext, Geometry, IEnumerable<MapData>> compiledIntersectQuery =
          //  EF.CompileQuery((PraxisContext context, Geometry place) => context.MapData.Where(md => md.place.Intersects(place)));

        public void MakePraxisDB()
        {
            PraxisContext db = new PraxisContext();
            if (!db.Database.EnsureCreated()) //all the automatic stuff EF does for us
                return;

            //Not automatic entries executed below:
            //PostgreSQL will make automatic spatial indexes
            if (serverMode == "PostgreSQL")
            {
                db.Database.ExecuteSqlRaw(GeneratedMapDataIndexPG);
                db.Database.ExecuteSqlRaw(MapTileIndexPG);
                db.Database.ExecuteSqlRaw(SlippyMapTileIndexPG);
                db.Database.ExecuteSqlRaw(StoredElementsIndexPG);
            }
            else //SQL Server and MariaDB share the same syntax here
            {
                db.Database.ExecuteSqlRaw(GeneratedMapDataIndex);
                db.Database.ExecuteSqlRaw(MapTileIndex);
                db.Database.ExecuteSqlRaw(SlippyMapTileIndex);
                db.Database.ExecuteSqlRaw(StoredElementsIndex);
            }

            if (serverMode == "SQLServer")
            {
                db.Database.ExecuteSqlRaw(GeneratedMapDataValidTriggerMSSQL);
            }
            if (serverMode == "MariaDB")
            {
                db.Database.ExecuteSqlRaw("SET collation_server = 'utf8mb4_unicode_ci'; SET character_set_server = 'utf8mb4'"); //MariaDB defaults to latin2_swedish, we need Unicode.
            }

            InsertDefaultServerConfig();
            InsertDefaultFactionsToDb();
            InsertDefaultPaintTownConfigs();
            InsertDefaultStyle();
        }

        public static void InsertDefaultFactionsToDb()
        {
            var db = new PraxisContext();

            if (serverMode == "SQLServer")
            {
                db.Database.BeginTransaction();
                db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT Factions ON;");
            }
            db.Factions.AddRange(Singletons.defaultFaction);
            db.SaveChanges();
            if (serverMode == "SQLServer")
            {
                db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT Factions OFF;");
                db.Database.CommitTransaction();
            }
        }

        public static void InsertDefaultServerConfig()
        {
            var db = new PraxisContext();
            db.ServerSettings.Add(new ServerSetting() { NorthBound = 90, SouthBound = -90, EastBound = 180, WestBound = -180, SlippyMapTileSizeSquare = 512 });
            db.SaveChanges();
        }

        public static void InsertDefaultStyle()
        {
            var db = new PraxisContext();
            //Remove any existing entries, in case I'm refreshing the rules on an existing entry.
            if (serverMode != "PostgreSQL") //PostgreSQL has stricter requirements on its syntax.
            {
                //db.Database.ExecuteSqlRaw("DELETE FROM TagParserEntriesTagParserMatchRules");
                //db.Database.ExecuteSqlRaw("DELETE FROM TagParserEntries");
                //db.Database.ExecuteSqlRaw("DELETE FROM TagParserMatchRules");
            }

            if (serverMode == "SQLServer")
            {
                db.Database.BeginTransaction();
                db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT TagParserEntries ON;");
            }
            db.TagParserEntries.AddRange(Singletons.defaultTagParserEntries);
            db.SaveChanges();
            if (serverMode == "SQLServer")
            {
                db.Database.ExecuteSqlRaw("SET IDENTITY_INSERT TagParserEntries OFF;");
                db.Database.CommitTransaction();
            }
        }

        public static void InsertDefaultPaintTownConfigs()
        {
            var db = new PraxisContext();
            //we set the reset time to next Saturday at midnight for a default.
            var nextSaturday = DateTime.Now.AddDays(6 - (int)DateTime.Now.DayOfWeek);
            nextSaturday.AddHours(-nextSaturday.Hour);
            nextSaturday.AddMinutes(-nextSaturday.Minute);
            nextSaturday.AddSeconds(-nextSaturday.Second);

            var tomorrow = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day).AddDays(1);
            db.PaintTownConfigs.Add(new PaintTownConfig() { Name = "All-Time", Cell10LockoutTimer = 300, DurationHours = -1, NextReset = nextSaturday });
            db.PaintTownConfigs.Add(new PaintTownConfig() { Name = "Weekly", Cell10LockoutTimer = 300, DurationHours = 168, NextReset = nextSaturday });

            //PaintTheTown requires dummy entries in the playerData table, or it doesn't know which factions exist. It's faster to do this once here than to check on every call to playerData
            foreach (var faction in Singletons.defaultFaction)
                db.PlayerData.Add(new PlayerData() { deviceID = "dummy" + faction.FactionId, FactionId = faction.FactionId });
            db.SaveChanges();
        }
    }
}

