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

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            //TODO: figure out this connection string for local testing, and for AWS use.
            //LocalHost
            optionsBuilder.UseSqlServer(@"Data Source=DESKTOP-B977OTE\SQLEXPRESS;Integrated Security = true;Initial Catalog=GpsExplore;", x => x.UseNetTopologySuite());
            //NetTopologySuite is for future location stuff from OSM data.
        }
    }
}
