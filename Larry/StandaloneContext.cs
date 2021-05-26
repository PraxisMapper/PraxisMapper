using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Larry
{
    public partial class StandaloneContext : DbContext
    {
        public string destinationFilename = "Standalone.sqlite";

        public virtual DbSet<MapTileDB> MapTiles { get; set; }
        public virtual DbSet<TerrainInfo> TerrainInfo { get; set; }
        public virtual DbSet<Bounds> Bounds { get; set; }
        public virtual DbSet<PlusCodesVisited> PlusCodesVisited { get; set; }
        public virtual DbSet<PlayerStats> PlayerStats { get; set; }

        public virtual DbSet<ScavengerHunt> ScavengerHunts { get; set; }

        public StandaloneContext()
        {
        }

        public StandaloneContext(string destFile)
        {
            destinationFilename = destFile + ".sqlite";
        }

        public StandaloneContext(DbContextOptions<StandaloneContext> options, string destFile)
            : base(options)
        {
            destinationFilename = destFile + ".sqlite";
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlite("Data Source=" + destinationFilename); // Standalone.sqlite");
            }
        }

        protected override void OnModelCreating(ModelBuilder model)
        {
            //set indexed and names and such here.
            model.Entity<MapTileDB>().HasIndex(p => p.PlusCode);

            model.Entity<TerrainInfo>().HasIndex(p => p.PlusCode);

            model.Entity<PlusCodesVisited>().HasIndex(p => p.PlusCode);

        }

    }

    //DB Tables below here
    public class MapTileDB //read-only for the destination app
    {
        public long id { get; set; }
        public string PlusCode { get; set; }
        public byte[] image { get; set; }
        public int layer { get; set; } //I might want to do variant maptiles where each area cliamed adds an overlay to the base map tile, this tracks which stacking order this goes in.

    }

    public class TerrainInfo //read-only for the destination app. writes go to PlusCodesVisited
    {
        public long id { get; set; }
        public string PlusCode { get; set; }
        public string Name { get; set; }
        public string areaType { get; set; } //the game element name
        public long OsmElementId { get; set; } //Might need to be a long. Might be irrelevant on self-contained DB (except maybe for loading an overlay image on a maptile?)
        public long OsmElementType { get; set; } //Could be unnecessary on the standalone DB.
    }

    public class Bounds //readonly for the destination app
    {
        public int id { get; set; }
        public double NorthBound { get; set; }
        public double SouthBound { get; set; }
        public double EastBound { get; set; }
        public double WestBound { get; set; }

    }

    public class PlusCodesVisited //read-write. has bool for any visit, last visit date, is used for daily/weekly checks, and PaintTheTown mode.
    {
        public int id { get; set; }
        public string PlusCode { get; set; }
        public int visited { get; set; } //0 for false, 1 for true
        public DateTime lastVisit { get; set; }
        public DateTime nextDailyBonus { get; set; }
        public DateTime nextWeeklyBonus { get; set; }
    }

    public class PlayerStats //read-write, doesn't leave the device
    {
        public int id { get; set; }
        public int timePlayed { get; set; }
        public double distanceWalked { get; set; }
        //public double AreasVisited { get; set; } //get count() from pluscodesvisited wwhere visited = 1


    }

    public class ScavengerHunt
    { 
        public int id { get; set; }
        //public int listId { get; set; } //Which list does this entry appear on. A 
        public string listName { get; set; } //using name as an ID, to avoid needed a separate table thats just ids and names.
        public string description { get; set; } //Defaults to element name on auto-generated lists. Users could make this hints or clues instead.
        public bool playerHasVisited { get; set; } //Single player mode means I can store this inline.
        public long OsmElementId { get; set; } //Reference to see what this thing is in the source data. Empty for user-created items.
        public long OsmElementType { get; set; } //as above.

    }


    //public class WeeklyVisited //read-write
    //{ 
    //}

    //public class DailyVisited //read-write
    //{

    //}

}
