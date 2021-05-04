using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
using static CoreComponents.DbTables;

namespace CoreComponents
{
    public class PraxisContext : DbContext
    {
        public DbSet<PlayerData> PlayerData { get; set; }
        public DbSet<PerformanceInfo> PerformanceInfo { get; set; }
        public DbSet<AreaType> AreaTypes { get; set; }
        public DbSet<MapData> MapData { get; set; }
        public DbSet<AdminBound> AdminBounds { get; set; } //Identical to MapData, but only for entries where AreaTypeId == 13. Should help performance a good amount.
        public DbSet<MapTile> MapTiles { get; set; }

        public DbSet<SlippyMapTile> SlippyMapTiles { get; set; }
        public DbSet<Faction> Factions { get; set; }
        public DbSet<AreaControlTeam> AreaControlTeams { get; set; } //This is TeamClaims, rename this.
        public DbSet<GeneratedMapData> GeneratedMapData { get; set; } 
        public DbSet<PaintTownConfig> PaintTownConfigs { get; set; }
        public DbSet<PaintTownEntry> PaintTownEntries { get; set; }
        public DbSet<PaintTownScoreRecord> PaintTownScoreRecords { get; set; }
        public DbSet<ErrorLog> ErrorLogs { get; set; }
        public DbSet<ServerSetting>  ServerSettings { get; set; }
        public DbSet<TileTracking> TileTrackings { get; set; }
        public DbSet<ZztGame> ZztGames { get; set; }
        public DbSet<GamesBeaten> GamesBeaten { get; set; }

        public DbSet<StoredNode> StoredNodes { get; set; }
        public DbSet<StoredWay> StoredWays { get; set; }
        public DbSet<StoredRelation> StoredRelations { get; set; }

        //IConfiguration Config;
        public static string connectionString = "Data Source=localhost\\SQLDEV;UID=GpsExploreService;PWD=lamepassword;Initial Catalog=Praxis;"; //Needs a default value.
        public static string serverMode = "SQLServer";

        //Test table to see if its practical to save prerendered results. there's 25 million 6codes, so no.
        //public DbSet<PremadeResults> PremadeResults { get; set; }

        //Test table for loading osm data directly in to the DB with less processing.
        //Takes up a lot more storage space this way, not as useful for app purposes. Removing
        //public DbSet<MinimumNode> MinimumNodes { get; set; }
        //public DbSet<MinimumWay> MinimumWays { get; set; }
        //public DbSet<MinimumRelation> minimumRelations { get; set; }
        
        //public static MemoryCache mc = new MemoryCache(new MemoryCacheOptions()); //Docs on this are poor, and don't explain what EFCore will actually cache. I think it's queries, not results.

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (serverMode == "SQLServer")
                optionsBuilder.UseSqlServer(connectionString, x => x.UseNetTopologySuite());
            else if (serverMode == "MariaDB")
            {
                optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), x => x.UseNetTopologySuite().EnableRetryOnFailure());
            }

            //optionsBuilder.UseMemoryCache(mc);//I think this improves performance at the cost of RAM usage. Needs additional testing.
        }

        protected override void OnModelCreating(ModelBuilder model)
        {
            //Create indexes here.
            model.Entity<PlayerData>().HasIndex(p => p.deviceID); //for updating data

            //for checking OSM data, and allowing items to be updated instead of simply replaced in the future.
            model.Entity<MapData>().HasIndex(p => p.WayId); 
            model.Entity<MapData>().HasIndex(p => p.RelationId); 
            model.Entity<MapData>().HasIndex(p => p.NodeId); 
            model.Entity<MapData>().HasIndex(p => p.AreaTypeId); //At the least, helpful for sorting out admin entries from others.
            model.Entity<MapData>().HasIndex(p => p.AreaSize); //Used as a filter when drawing larger area maptiles. Tell the DB not to load points smaller than 1 pixel. This is in degrees for lines, degrees squared for areas and points.
            //generatedMapData only gets searched on its primary key and place (which has an index defined elsewhere at creation)

            //for checking OSM data, and allowing items to be updated instead of simply replaced in the future.
            model.Entity<AdminBound>().HasIndex(p => p.WayId);
            model.Entity<AdminBound>().HasIndex(p => p.RelationId);
            model.Entity<AdminBound>().HasIndex(p => p.NodeId);
            model.Entity<AdminBound>().HasIndex(p => p.AreaTypeId); //At the least, helpful for sorting out admin entries from others.
            model.Entity<AdminBound>().HasIndex(p => p.AreaSize); //Used as a filter when drawing larger area maptiles. Tell the DB not to load points smaller than 1 pixel. This is in degrees for lines, degrees squared for areas and points.

            model.Entity<MapTile>().HasIndex(m => m.PlusCode);
            model.Entity<MapTile>().Property(m => m.PlusCode).HasMaxLength(12);
            model.Entity<MapTile>().HasIndex(m => m.mode);

            model.Entity<SlippyMapTile>().HasIndex(m => m.Values);
            model.Entity<SlippyMapTile>().HasIndex(m => m.mode);

            model.Entity<AreaControlTeam>().HasIndex(m => m.MapDataId);
            model.Entity<AreaControlTeam>().HasIndex(m => m.FactionId);

            model.Entity<PaintTownEntry>().HasIndex(m => m.FactionId);
            model.Entity<PaintTownEntry>().HasIndex(m => m.PaintTownConfigId);
            model.Entity<PaintTownEntry>().HasIndex(m => m.Cell8); //index for looking up current tiles.
            model.Entity<PaintTownEntry>().HasIndex(m => m.Cell10); //index for claiming
            //No index on the claimedAt column, since the Cell8Recent call should use the Cell8 index, and then scanning over a max of 400 entries shouldn't be terrible. Add that here if this assumption is wrong.

            model.Entity<PaintTownScoreRecord>().HasIndex(m => m.PaintTownConfigId);
            model.Entity<PaintTownScoreRecord>().HasIndex(m => m.WinningFactionID);
        }

        //A trigger to ensure all data inserted is valid by SQL Server rules.
        public static string MapDataValidTriggerMSSQL = "CREATE TRIGGER dbo.MakeValid ON dbo.MapData AFTER INSERT AS BEGIN UPDATE dbo.MapData SET place = place.MakeValid() WHERE MapDataId in (SELECT MapDataId from inserted) END";
        public static string GeneratedMapDataValidTriggerMSSQL = "CREATE TRIGGER dbo.GenereatedMapDataMakeValid ON dbo.GeneratedMapData AFTER INSERT AS BEGIN UPDATE dbo.GeneratedMapData SET place = place.MakeValid() WHERE GeneratedMapDataId in (SELECT GeneratedMapDataId from inserted) END";
        
        //An index that I don't think EFCore can create correctly automatically.
        public static string MapDataIndex = "CREATE SPATIAL INDEX MapDataSpatialIndex ON MapData(place)";
        public static string GeneratedMapDataIndex = "CREATE SPATIAL INDEX GeneratedMapDataSpatialIndex ON GeneratedMapData(place)";
        public static string MapTileIndex = "CREATE SPATIAL INDEX MapTileSpatialIndex ON MapTiles(areaCovered)";
        public static string SlippyMapTileIndex = "CREATE SPATIAL INDEX SlippyMapTileSpatialIndex ON SlippyMapTiles(areaCovered)";
        //TODO: add spatial index to StoredNode/way/relation tables.

        //MariaDB may not actually require these triggers. MSSQL insists on its data passing its own validity check, MariaDB appears not to so far. Keeping these for now in case I'm wrong.
        //public static string MapDataValidTriggerMariaDB= "CREATE TRIGGER dbo.MakeValid ON dbo.MapData AFTER INSERT AS BEGIN UPDATE dbo.MapData SET place = place.MakeValid() WHERE MapDataId in (SELECT MapDataId from inserted) END";
        //public static string GeneratedMapDataValidTriggerMariaDB = "CREATE TRIGGER dbo.GenereatedMapDataMakeValid ON dbo.GeneratedMapData AFTER INSERT AS BEGIN UPDATE dbo.GeneratedMapData SET place = place.MakeValid() WHERE GeneratedMapDataId in (SELECT GeneratedMapDataId from inserted) END";
        //public static string FindDBMapDataBoundsMariaDB = "CREATE PROCEDURE GetMapDataBounds AS BEGIN SELECT MIN(CONVERT(float, geography::STGeomFromWKB(geometry::STGeomFromWKB(place.STAsBinary(), place.STSrid).MakeValid().STEnvelope().STAsBinary(), place.STSrid).MakeValid().STPointN(1).Long)) as minimumPointLon, MIN(CONVERT(float, geography::STGeomFromWKB(geometry::STGeomFromWKB(place.STAsBinary(), place.STSrid).MakeValid().STEnvelope().STAsBinary(), place.STSrid).MakeValid().STPointN(1).Lat)) as minimumPointLat, MAX(CONVERT(float, geography::STGeomFromWKB(geometry::STGeomFromWKB(place.STAsBinary(), place.STSrid).MakeValid().STEnvelope().STAsBinary(), place.STSrid).MakeValid().STPointN(3).Long)) as maximumPointLon, MAX(CONVERT(float, geography::STGeomFromWKB(geometry::STGeomFromWKB(place.STAsBinary(), place.STSrid).MakeValid().STEnvelope().STAsBinary(), place.STSrid).MakeValid().STPointN(3).Lat)) as maximumPointLatFROM mapdata END";
        //This sproc below is marginally faster than an insert with changetracking off (~.7 ms on average). Excluding to keep code consistent and EFCore-only where possible.
        //public static string PerformanceInfoSproc = "CREATE PROCEDURE SavePerfInfo @functionName nvarchar(500), @runtime bigint, @calledAt datetime2, @notes nvarchar(max) AS BEGIN INSERT INTO dbo.PerformanceInfo(functionName, runTime, calledAt, notes) VALUES(@functionName, @runtime, @calledAt, @notes) END";

        //This doesn't appear to be any faster. The query isn't the slow part. Keeping this code as a reference for how to precompile queries.
        public static Func<PraxisContext, Geometry, IEnumerable<MapData>> compiledIntersectQuery = 
            EF.CompileQuery((PraxisContext context, Geometry place) =>  context.MapData.Where(md => md.place.Intersects(place)));
    }
}

