using Microsoft.EntityFrameworkCore;
using static CoreComponents.StandaloneDbTables;

namespace Larry
{
    public partial class StandaloneContext : DbContext
    {
        public string destinationFilename = "Standalone.sqlite";
        //Solar2D's SQLite library only allows for one DB to be open at once. This means my idea to have a split set of DB files will 
        //not be the best plan for performance, since i'd have to open and close the DB connection on every query.
        //So, I'll choose Plan B: save everything into one database, and make a copy of the DB in user-writable space on the device.
        //This is a little redundant on space usage, but a big county is ~2 MB for the database.
        public virtual DbSet<MapTileDB> MapTiles { get; set; }
        public virtual DbSet<TerrainInfo> TerrainInfo { get; set; }
        public virtual DbSet<Bounds> Bounds { get; set; }
        public virtual DbSet<PlusCodesVisited> PlusCodesVisited { get; set; }
        public virtual DbSet<PlayerStats> PlayerStats { get; set; }
        public virtual DbSet<ScavengerHuntStandalone> ScavengerHunts { get; set; }
        public virtual DbSet<PlaceInfo2> PlaceInfo2s { get; set; }
        public virtual DbSet<PlaceIndex> PlaceIndexs { get; set; }
        public virtual DbSet<IdleStats> IdleStats { get; set; }

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
            
            model.Entity<TerrainDataSmall>().HasIndex(p => p.Name);

            model.Entity<PlusCodesVisited>().HasIndex(p => p.PlusCode);

            model.Entity<ScavengerHuntStandalone>().HasIndex(p => p.playerHasVisited);
        }

    }
}
