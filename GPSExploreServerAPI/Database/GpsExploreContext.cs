using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GPSExploreServerAPI.Database
{
    public class GpsExploreContext : DbContext
    {
        public DbSet<PlayerData> PlayerData { get; set; }
        public DbSet<PerformanceInfo> PerformanceInfo { get; set; }

        //public DbSet<MapData> MapData { get; set; } //uncomment this when I've advanced onto the map processing project.
        //add performance info table

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            //TODO: figure out this connection string for local testing, and for AWS use.
            //LocalHost
            optionsBuilder.UseSqlServer(@"Data Source=localhost\SQLEXPRESS;Integrated Security = true;Initial Catalog=GpsExplore;", x => x.UseNetTopologySuite()); //Home config
            //NetTopologySuite is for future location stuff from OSM data.
        }

        protected override void OnModelCreating(ModelBuilder model)
        {
            //Create indexes here.
            model.Entity<PlayerData>().HasIndex(p => p.deviceID); //for updating data
        }
    }
}
