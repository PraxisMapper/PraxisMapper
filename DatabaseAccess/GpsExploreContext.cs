using Microsoft.EntityFrameworkCore;
using System;
using static DatabaseAccess.DbTables;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Microsoft.EntityFrameworkCore.Update;

namespace DatabaseAccess
{
    public class GpsExploreContext : DbContext
    {
        public DbSet<PlayerData> PlayerData { get; set; }
        public DbSet<PerformanceInfo> PerformanceInfo { get; set; }
        public DbSet<AreaType> AreaTypes { get; set; }
        public DbSet<MapData> MapData { get; set; }
        //Test table to see if its practical to save prerendered results. there's 25 million 6codes, so no.
        public DbSet<PremadeResults> PremadeResults { get; set; }

        public DbSet<SinglePointsOfInterest> SinglePointsOfInterests { get; set; }


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
            
        }

        protected override void OnModelCreating(ModelBuilder model)
        {
            //Create indexes here.
            model.Entity<PlayerData>().HasIndex(p => p.deviceID); //for updating data

            model.Entity<SinglePointsOfInterest>().HasIndex(i => i.PlusCode); //for reading data
            model.Entity<SinglePointsOfInterest>().HasIndex(i => i.PlusCode8); //for reading data, previously used.
            model.Entity<SinglePointsOfInterest>().HasIndex(i => i.PlusCode6); //for reading data, but actually used.
            model.Entity<SinglePointsOfInterest>().HasIndex(i => i.NodeID); //for finding and removing duplicates, primarily
            model.Entity<SinglePointsOfInterest>().Property(i => i.PlusCode8).HasMaxLength(8);
            model.Entity<SinglePointsOfInterest>().Property(i => i.PlusCode6).HasMaxLength(6);
            model.Entity<SinglePointsOfInterest>().Property(i => i.PlusCode).HasMaxLength(15);

            model.Entity<MapData>().HasIndex(p => p.WayId); //for checking OSM data and cleaning dupes

            //Table for testing if its faster/easier/smaller to just save the results directly to a DB.
            model.Entity<PremadeResults>().HasIndex(p => p.PlusCode6);
            model.Entity<PremadeResults>().Property(p => p.PlusCode6).HasMaxLength(6);
        }

        public static string MapDataValidTrigger = "CREATE TRIGGER dbo.MakeValid ON dbo.MapData AFTER INSERT AS BEGIN UPDATE dbo.MapData SET place = place.MakeValid() WHERE MapDataId in (SELECT MapDataId from inserted) END";
        public static string MapDataIndex = "CREATE SPATIAL INDEX MapDataSpatialIndex ON MapData(place)";
    }
}
