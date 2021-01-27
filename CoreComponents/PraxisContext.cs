using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
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
        public DbSet<MapTile> MapTiles { get; set; }
        public DbSet<Faction> Factions { get; set; }
        public DbSet<AreaControlTeam> AreaControlTeams { get; set; } //This is TeamClaims, rename this.
        public DbSet<GeneratedMapData> GeneratedMapData { get; set; } 
        public DbSet<TurfWarConfig> TurfWarConfigs { get; set; }
        public DbSet<TurfWarEntry> TurfWarEntries { get; set; }
        public DbSet<TurfWarScoreRecord> TurfWarScoreRecords { get; set; }
        public DbSet<TurfWarTeamAssignment> TurfWarTeamAssignments { get; set; }
        public DbSet<ErrorLog> ErrorLogs { get; set; }
        public DbSet<ServerSetting>  ServerSettings { get; set; }
        public DbSet<TileTracking> TileTrackings { get; set; }


        //IConfiguration Config;
        public static string connectionString = "Data Source=localhost\\SQLDEV;UID=GpsExploreService;PWD=lamepassword;Initial Catalog=Praxis;"; //Needs a default value.
        public static string serverMode = "SQLServer";

        //Test table to see if its practical to save prerendered results. there's 25 million 6codes, so no.
        public DbSet<PremadeResults> PremadeResults { get; set; }

        //Test table for loading osm data directly in to the DB with less processing.
        //Takes up a lot more storage space this way, not as useful for app purposes. Removing
        //public DbSet<MinimumNode> MinimumNodes { get; set; }
        //public DbSet<MinimumWay> MinimumWays { get; set; }
        //public DbSet<MinimumRelation> minimumRelations { get; set; }

        //public PraxisContext(string conString)
        //{
            //connectionString = conString;
        //}

        public static MemoryCache mc = new MemoryCache(new MemoryCacheOptions()); 

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            //TODO: figure out this connection string for local testing, and for AWS use.
            //This is the next top priority.
            if (serverMode == "SQLServer")
                optionsBuilder.UseSqlServer(connectionString, x => x.UseNetTopologySuite());
            else if (serverMode == "MariaDB")
            {
                optionsBuilder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), x => x.UseNetTopologySuite().CharSetBehavior(Pomelo.EntityFrameworkCore.MySql.Infrastructure.CharSetBehavior.NeverAppend)); //, x => x."Server=localhost;Database=praxis;User=root;Password=1234;");
            }

            //Current server config
            //optionsBuilder.UseSqlServer(@"Data Source=localhost\SQLEXPRESS;UID=GpsExploreService;PWD=lamepassword;Initial Catalog=Praxis;", x => x.UseNetTopologySuite());
            //Current localhost config.
            //optionsBuilder.UseSqlServer(@"Data Source=localhost\SQLDEV;UID=GpsExploreService;PWD=lamepassword;Initial Catalog=Praxis;", x => x.UseNetTopologySuite()); //Home config, SQL Developer, Free, no limits, cant use in production

            //SQLite config should be used for the case where I make a self-contained app for an area.
            //like for a university or a park or something.           

            optionsBuilder.UseMemoryCache(mc);//I think this improves performance at the cost of RAM usage. Needs additional testing.
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
            //generatedMapData only gets searched on its primary key.

            //Table for testing if its faster/easier/smaller to just save the results directly to a DB.
            //It is not, at least not at my current scale since this is 25 million 6-cells. Takes ~9 days on a single PC.
            model.Entity<PremadeResults>().HasIndex(p => p.PlusCode6);
            model.Entity<PremadeResults>().Property(p => p.PlusCode6).HasMaxLength(6);

            model.Entity<MapTile>().HasIndex(m => m.PlusCode);
            model.Entity<MapTile>().Property(m => m.PlusCode).HasMaxLength(12);

            model.Entity<AreaControlTeam>().HasIndex(m => m.MapDataId);
            model.Entity<AreaControlTeam>().HasIndex(m => m.FactionId);

            model.Entity<TurfWarEntry>().HasIndex(m => m.FactionId);
            model.Entity<TurfWarEntry>().HasIndex(m => m.TurfWarConfigId);
            model.Entity<TurfWarEntry>().HasIndex(m => m.Cell8); //index for looking up current tiles.
            model.Entity<TurfWarEntry>().HasIndex(m => m.Cell10); //index for claiming

            model.Entity<TurfWarScoreRecord>().HasIndex(m => m.TurfWarConfigId);
            model.Entity<TurfWarScoreRecord>().HasIndex(m => m.WinningFactionID);

            model.Entity<TurfWarTeamAssignment>().HasIndex(m => m.FactionId);
            model.Entity<TurfWarTeamAssignment>().HasIndex(m => m.TurfWarConfigId);
            model.Entity<TurfWarTeamAssignment>().HasIndex(m => m.deviceID);
        }

        //A trigger to ensure all data inserted is valid by SQL Server rules.
        public static string MapDataValidTriggerMSSQL = "CREATE TRIGGER dbo.MakeValid ON dbo.MapData AFTER INSERT AS BEGIN UPDATE dbo.MapData SET place = place.MakeValid() WHERE MapDataId in (SELECT MapDataId from inserted) END";
        public static string GeneratedMapDataValidTriggerMSSQL = "CREATE TRIGGER dbo.GenereatedMapDataMakeValid ON dbo.GeneratedMapData AFTER INSERT AS BEGIN UPDATE dbo.GeneratedMapData SET place = place.MakeValid() WHERE GeneratedMapDataId in (SELECT GeneratedMapDataId from inserted) END";
        //An index that I don't think EFCore can create correctly automatically.
        public static string MapDataIndex = "CREATE SPATIAL INDEX MapDataSpatialIndex ON MapData(place)";
        public static string GeneratedMapDataIndex = "CREATE SPATIAL INDEX GeneratedMapDataSpatialIndex ON GeneratedMapData(place)";
        //This sproc pulls the min/max points covered by the current database. Useful if you want to create maptiles ahead of time. Can take a few minutes to run.
        public static string FindDBMapDataBoundsMSSQL = "CREATE PROCEDURE GetMapDataBounds AS BEGIN SELECT MIN(CONVERT(float, geography::STGeomFromWKB(geometry::STGeomFromWKB(place.STAsBinary(), place.STSrid).MakeValid().STEnvelope().STAsBinary(), place.STSrid).MakeValid().STPointN(1).Long)) as minimumPointLon, MIN(CONVERT(float, geography::STGeomFromWKB(geometry::STGeomFromWKB(place.STAsBinary(), place.STSrid).MakeValid().STEnvelope().STAsBinary(), place.STSrid).MakeValid().STPointN(1).Lat)) as minimumPointLat, MAX(CONVERT(float, geography::STGeomFromWKB(geometry::STGeomFromWKB(place.STAsBinary(), place.STSrid).MakeValid().STEnvelope().STAsBinary(), place.STSrid).MakeValid().STPointN(3).Long)) as maximumPointLon, MAX(CONVERT(float, geography::STGeomFromWKB(geometry::STGeomFromWKB(place.STAsBinary(), place.STSrid).MakeValid().STEnvelope().STAsBinary(), place.STSrid).MakeValid().STPointN(3).Lat)) as maximumPointLatFROM mapdata END";
        //Results on current Ohio data set:
        //minimumPointLon	minimumPointLat	maximumPointLon	maximumPointLat
        //-85.111908	38.1447449	-80.3286743    42.070076

        //MariaDB may not actually require these triggers. MSSQL insists on its data passing its own validity check, MariaDB appears not to so far. Keeping these for now in case I'm wrong.
        //public static string MapDataValidTriggerMariaDB= "CREATE TRIGGER dbo.MakeValid ON dbo.MapData AFTER INSERT AS BEGIN UPDATE dbo.MapData SET place = place.MakeValid() WHERE MapDataId in (SELECT MapDataId from inserted) END";
        //public static string GeneratedMapDataValidTriggerMariaDB = "CREATE TRIGGER dbo.GenereatedMapDataMakeValid ON dbo.GeneratedMapData AFTER INSERT AS BEGIN UPDATE dbo.GeneratedMapData SET place = place.MakeValid() WHERE GeneratedMapDataId in (SELECT GeneratedMapDataId from inserted) END";
        //public static string FindDBMapDataBoundsMariaDB = "CREATE PROCEDURE GetMapDataBounds AS BEGIN SELECT MIN(CONVERT(float, geography::STGeomFromWKB(geometry::STGeomFromWKB(place.STAsBinary(), place.STSrid).MakeValid().STEnvelope().STAsBinary(), place.STSrid).MakeValid().STPointN(1).Long)) as minimumPointLon, MIN(CONVERT(float, geography::STGeomFromWKB(geometry::STGeomFromWKB(place.STAsBinary(), place.STSrid).MakeValid().STEnvelope().STAsBinary(), place.STSrid).MakeValid().STPointN(1).Lat)) as minimumPointLat, MAX(CONVERT(float, geography::STGeomFromWKB(geometry::STGeomFromWKB(place.STAsBinary(), place.STSrid).MakeValid().STEnvelope().STAsBinary(), place.STSrid).MakeValid().STPointN(3).Long)) as maximumPointLon, MAX(CONVERT(float, geography::STGeomFromWKB(geometry::STGeomFromWKB(place.STAsBinary(), place.STSrid).MakeValid().STEnvelope().STAsBinary(), place.STSrid).MakeValid().STPointN(3).Lat)) as maximumPointLatFROM mapdata END";
        //This sproc below is marginally faster than an insert with changetracking off (~.7 ms on average). Excluding to keep code consistent and EFCore-only where possible.
        //public static string PerformanceInfoSproc = "CREATE PROCEDURE SavePerfInfo @functionName nvarchar(500), @runtime bigint, @calledAt datetime2, @notes nvarchar(max) AS BEGIN INSERT INTO dbo.PerformanceInfo(functionName, runTime, calledAt, notes) VALUES(@functionName, @runtime, @calledAt, @notes) END";

        //This doesn't appear to be any faster. The query isn't the slow part. Keeping this code as a reference for how to precompile queries.
        public static Func<PraxisContext, Geometry, IEnumerable<MapData>> compiledIntersectQuery = 
            EF.CompileQuery((PraxisContext context, Geometry place) =>  context.MapData.Where(md => md.place.Intersects(place)));

        //TODO: This isn't used, remove it.
        public IEnumerable<MapData> getPlaces(Geometry place)
        {
            return compiledIntersectQuery(this, place);
        }
    }
}

