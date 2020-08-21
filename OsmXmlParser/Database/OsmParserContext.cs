using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OsmXmlParser.Database
{
    public class OsmParserContext : DbContext
    {

        public DbSet<InterestingPoint> InterestingPoints { get; set; }
        public DbSet<ProcessedWay> ProcessedWays { get; set; }
        public DbSet<AreaType> AreaTypes { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            //TODO: figure out this connection string for local testing, and for AWS use.
            //LocalHost
            optionsBuilder.UseSqlServer(@"Data Source=localhost\SQLEXPRESS;Integrated Security = true;Initial Catalog=OsmServer;", x => x.UseNetTopologySuite()); //Home config
        }

        protected override void OnModelCreating(ModelBuilder model)
        {
            //Create indexes here.
            model.Entity<ProcessedWay>().HasIndex(p => p.OsmWayId); //for updating data
            model.Entity<InterestingPoint>().HasIndex(i => i.PlusCode8); //for reading data

            model.Entity<InterestingPoint>().Property(i => i.PlusCode8).HasMaxLength(8);
            model.Entity<InterestingPoint>().Property(i => i.PlusCode2).HasMaxLength(2);
        }

    }
}
