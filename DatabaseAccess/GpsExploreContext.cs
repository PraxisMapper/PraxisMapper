using Microsoft.EntityFrameworkCore;
using System;
using static DatabaseAccess.DbTables;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Microsoft.EntityFrameworkCore.Update;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using NetTopologySuite.Geometries;
using System.Linq;
using Microsoft.VisualBasic.CompilerServices;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DatabaseAccess
{
    public class GpsExploreContext : DbContext
    {
        public DbSet<PlayerData> PlayerData { get; set; }
        public DbSet<PerformanceInfo> PerformanceInfo { get; set; }
        public DbSet<AreaType> AreaTypes { get; set; }
        public DbSet<MapData> MapData { get; set; }

        public DbSet<MapTile> MapTiles { get; set; }

        IConfiguration Config;
        //Test table to see if its practical to save prerendered results. there's 25 million 6codes, so no.
        //public DbSet<PremadeResults> PremadeResults { get; set; }

        //Test table for loading osm data directly in to the DB with less processing.
        //Takes up a lot more storage space this way, not as useful for app purposes. Removing for now.
        //public DbSet<MinimumNode> MinimumNodes { get; set; }
        //public DbSet<MinimumWay> MinimumWays { get; set; }
        //public DbSet<MinimumRelation> minimumRelations { get; set; }

        //public GpsExploreContext(IConfiguration config)
        //{
            //Config = config;
        //}

        public static MemoryCache mc = new MemoryCache(new MemoryCacheOptions()); 

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            //TODO: figure out this connection string for local testing, and for AWS use.
            

            //Current server config
            //optionsBuilder.UseSqlServer(@"Data Source=localhost\SQLEXPRESS;UID=GpsExploreService;PWD=lamepassword;Initial Catalog=GpsExplore;", x => x.UseNetTopologySuite());
            //Current localhost config.
            optionsBuilder.UseSqlServer(@"Data Source=localhost\SQLDEV;UID=GpsExploreService;PWD=lamepassword;Initial Catalog=GpsExplore;", x => x.UseNetTopologySuite()); //Home config, SQL Developer, Free, no limits, cant use in production
            
            //Potential MariaDB config, which would be cheaper on AWS
            //But also doesn't seem to be .NET 5 ready or compatible yet.
            //optionsBuilder.UseMySql("Server=localhost;Database=gpsExplore;User=root;Password=1234;");

            //SQLite config should be used for the case where I make a self-contained app for an area.
            //like for a university or a park or something.           

            optionsBuilder.UseMemoryCache(mc);//I think this improves performance at the cost of RAM usage. Needs additional testing.
        }

        protected override void OnModelCreating(ModelBuilder model)
        {
            //Create indexes here.
            model.Entity<PlayerData>().HasIndex(p => p.deviceID); //for updating data

            model.Entity<MapData>().HasIndex(p => p.WayId); //for checking OSM data and cleaning dupes

            //Table for testing if its faster/easier/smaller to just save the results directly to a DB.
            //It is not, at least not at my current scale since this is 25 million 6-cells. Takes ~9 days on a single PC.
            model.Entity<PremadeResults>().HasIndex(p => p.PlusCode6);
            model.Entity<PremadeResults>().Property(p => p.PlusCode6).HasMaxLength(6);
        }

        public static string MapDataValidTrigger = "CREATE TRIGGER dbo.MakeValid ON dbo.MapData AFTER INSERT AS BEGIN UPDATE dbo.MapData SET place = place.MakeValid() WHERE MapDataId in (SELECT MapDataId from inserted) END";
        public static string MapDataIndex = "CREATE SPATIAL INDEX MapDataSpatialIndex ON MapData(place)";

        //This sproc is marginally faster than an insert with changetracking off (~.7 ms on average). Excluding to keep code consistent and EFCore-only where possible.
        //public static string PerformanceInfoSproc = "CREATE PROCEDURE SavePerfInfo @functionName nvarchar(500), @runtime bigint, @calledAt datetime2, @notes nvarchar(max) AS BEGIN INSERT INTO dbo.PerformanceInfo(functionName, runTime, calledAt, notes) VALUES(@functionName, @runtime, @calledAt, @notes) END";

        //This doesn't appear to be any faster. The query isn't the slow part.
        public static Func<GpsExploreContext, Geometry, IEnumerable<MapData>> compiledIntersectQuery = 
            EF.CompileQuery((GpsExploreContext context, Geometry place) =>  context.MapData.Where(md => md.place.Intersects(place)));

        public IEnumerable<MapData> getPlaces(Geometry place)
        {
            return compiledIntersectQuery(this, place);

        }
    }
}
