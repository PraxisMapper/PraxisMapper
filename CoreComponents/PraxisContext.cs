using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using System;
using static PraxisCore.DbTables;

namespace PraxisCore
{
    /// <summary>
    /// A self-contained database connector for everything PraxisMapper can do in its database.
    /// </summary>
    public class PraxisContext : DbContext
    {
        public DbSet<PlayerData> PlayerData { get; set; }
        public DbSet<PerformanceInfo> PerformanceInfo { get; set; }
        public DbSet<MapTile> MapTiles { get; set; }
        public DbSet<SlippyMapTile> SlippyMapTiles { get; set; }
        public DbSet<ErrorLog> ErrorLogs { get; set; }
        public DbSet<ServerSetting> ServerSettings { get; set; }
        public DbSet<TileTracking> TileTrackings { get; set; } //This table is for making a full planet's maptiles, and tracks which ones have been drawn or not drawn to allow resuming.
        //public DbSet<ZztGame> ZztGames { get; set; }
        //public DbSet<GamesBeaten> GamesBeaten { get; set; }
        public DbSet<StoredOsmElement> StoredOsmElements { get; set; }
        public DbSet<TagParserEntry> TagParserEntries { get; set; }
        public DbSet<TagParserMatchRule> TagParserMatchRules { get; set; }
        public DbSet<TagParserPaint> TagParserPaints { get; set; }
        public DbSet<ElementTags> ElementTags { get; set; } //This table is exposed so I can search it directly faster.
        public DbSet<CustomDataOsmElement> CustomDataOsmElements { get; set; }
        public DbSet<CustomDataPlusCode> CustomDataPlusCodes { get; set; }
        public DbSet<GlobalDataEntries> GlobalDataEntries { get; set; }
        public DbSet<TagParserStyleBitmap> TagParserStyleBitmaps { get; set; }

        public static string connectionString = "Data Source=localhost\\SQLDEV;UID=PraxisService;PWD=lamepassword;Initial Catalog=Praxis;"; //Needs a default value.
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
            model.Entity<PlayerData>().HasIndex(p => p.deviceID);
            model.Entity<PlayerData>().HasIndex(p => p.dataKey);

            model.Entity<StoredOsmElement>().HasIndex(m => m.AreaSize); //Enables server-side sorting on biggest-to-smallest draw order.
            model.Entity<StoredOsmElement>().HasIndex(m => m.sourceItemID);
            model.Entity<StoredOsmElement>().HasIndex(m => m.sourceItemType);
            model.Entity<StoredOsmElement>().HasIndex(m => m.privacyId);
            model.Entity<StoredOsmElement>().HasMany(m => m.Tags).WithOne(m => m.storedOsmElement).HasForeignKey(m => new { m.SourceItemId, m.SourceItemType }).HasPrincipalKey(m => new { m.sourceItemID, m.sourceItemType });

            model.Entity<MapTile>().HasIndex(m => m.PlusCode);
            model.Entity<MapTile>().Property(m => m.PlusCode).HasMaxLength(12);
            model.Entity<MapTile>().HasIndex(m => m.styleSet);

            model.Entity<SlippyMapTile>().HasIndex(m => m.Values);
            model.Entity<SlippyMapTile>().HasIndex(m => m.styleSet);

            model.Entity<ElementTags>().HasIndex(m => m.Key);
            model.Entity<ElementTags>().HasOne(m => m.storedOsmElement).WithMany(m => m.Tags).HasForeignKey(m => new { m.SourceItemId, m.SourceItemType }).HasPrincipalKey(m => new { m.sourceItemID, m.sourceItemType });

            model.Entity<CustomDataOsmElement>().HasIndex(m => m.dataKey);
            model.Entity<CustomDataOsmElement>().HasIndex(m => m.StoredOsmElementId);
            //model.Entity<CustomDataOsmElement>().HasIndex(m => m.privacyId);

            model.Entity<CustomDataPlusCode>().HasIndex(m => m.dataKey);
            model.Entity<CustomDataPlusCode>().HasIndex(m => m.PlusCode);
            model.Entity<CustomDataPlusCode>().HasIndex(m => m.geoAreaIndex);

            if (serverMode == "PostgreSQL")
            {
                model.HasPostgresExtension("postgis");
            }
        }

        //A trigger to ensure all data inserted is valid by MSSQL Server rules.
        public static string MapDataValidTriggerMSSQL = "CREATE TRIGGER dbo.MakeValid ON dbo.MapData AFTER INSERT AS BEGIN UPDATE dbo.MapData SET place = place.MakeValid() WHERE MapDataId in (SELECT MapDataId from inserted) END";
        public static string GeneratedMapDataValidTriggerMSSQL = "CREATE TRIGGER dbo.GenereatedMapDataMakeValid ON dbo.GeneratedMapData AFTER INSERT AS BEGIN UPDATE dbo.GeneratedMapData SET place = place.MakeValid() WHERE GeneratedMapDataId in (SELECT GeneratedMapDataId from inserted) END";

        //An index that I don't think EFCore can create correctly automatically.
        public static string MapTileIndex = "CREATE SPATIAL INDEX MapTileSpatialIndex ON MapTiles(areaCovered)";
        public static string SlippyMapTileIndex = "CREATE SPATIAL INDEX SlippyMapTileSpatialIndex ON SlippyMapTiles(areaCovered)";
        public static string StoredElementsIndex = "CREATE SPATIAL INDEX StoredOsmElementsIndex ON StoredOsmElements(elementGeometry)";
        public static string customDataPlusCodesIndex = "CREATE SPATIAL INDEX customDataPlusCodeSpatialIndex ON CustomDataPlusCodes(geoAreaIndex)";
        public static string areaSizeIndex = "CREATE OR REPLACE INDEX IX_StoredOsmElements_AreaSize on StoredOsmElements (AreaSize)";
        public static string privacyIdIndex = "CREATE OR REPLACE INDEX IX_StoredOsmElements_privacyId on StoredOsmElements (privacyId)";
        public static string sourceItemIdIndex = "CREATE OR REPLACE INDEX IX_StoredOsmElements_sourceItemID on StoredOsmElements (sourceItemID)";
        public static string sourceItemTypeIndex = "CREATE OR REPLACE INDEX IX_StoredOsmElements_sourceItemType on StoredOsmElements (sourceItemType)";
        public static string tagKeyIndex = "CREATE OR REPLACE INDEX IX_ElementTags_Key on ElementTags (`Key`)";


        //PostgreSQL uses its own CREATE INDEX syntax
        public static string MapTileIndexPG = "CREATE INDEX maptiles_geom_idx ON public.\"MapTiles\" USING GIST(\"areaCovered\")";
        public static string SlippyMapTileIndexPG = "CREATE INDEX slippmayptiles_geom_idx ON public.\"SlippyMapTiles\" USING GIST(\"areaCovered\")";
        public static string StoredElementsIndexPG = "CREATE INDEX storedOsmElements_geom_idx ON public.\"StoredOsmElements\" USING GIST(\"elementGeometry\")";

        //Adding these as helper values for large use cases. When inserting large amounts of data, it's probably worth removing indexes for the insert and re-adding them later.
        //(On a North-America file insert, this keeps insert speeds at about 2-3 seconds per block, whereas it creeps up consistently while indexes are updated per block.
        //Though, I also see better results there droping the single-column indexes as well, which would need re-created manually since those one are automatic.
        public static string DropMapTileIndex = "DROP INDEX IF EXISTS MapTileSpatialIndex on MapTiles";
        public static string DropSlippyMapTileIndex = "DROP INDEX IF EXISTS SlippyMapTileSpatialIndex on SlippyMapTiles";
        public static string DropStoredElementsIndex = "DROP INDEX IF EXISTS StoredOsmElementsIndex on StoredOsmElements";
        public static string DropcustomDataPlusCodesIndex = "DROP INDEX IF EXISTS customDataPlusCodeSpatialIndex on CustomDataPlusCodes";
        public static string DropStoredElementsAreaSizeIndex = "DROP INDEX IF EXISTS IX_StoredOsmElements_AreaSize on StoredOsmElements";
        public static string DropStoredElementsPrivacyIdIndex = "DROP INDEX IF EXISTS IX_StoredOsmElements_privacyId on StoredOsmElements";
        public static string DropStoredElementsSourceItemIdIndex = "DROP INDEX IF EXISTS IX_StoredOsmElements_sourceItemID on StoredOsmElements";
        public static string DropStoredElementsSourceItemTypeIndex = "DROP INDEX IF EXISTS IX_StoredOsmElements_sourceItemType on StoredOsmElements";
        public static string DropTagKeyIndex = "DROP INDEX IF EXISTS IX_ElementTags_Key on ElementTags";



        //This doesn't appear to be any faster. The query isn't the slow part. Keeping this code as a reference for how to precompile queries.
        //public static Func<PraxisContext, Geometry, IEnumerable<MapData>> compiledIntersectQuery =
        //  EF.CompileQuery((PraxisContext context, Geometry place) => context.MapData.Where(md => md.place.Intersects(place)));

        public void MakePraxisDB()
        {
            //PraxisContext db = new PraxisContext();
            if (!Database.EnsureCreated()) //all the automatic stuff EF does for us
                return;

            //Not automatic entries executed below:
            //PostgreSQL will make automatic spatial indexes
            if (serverMode == "PostgreSQL")
            {
                //db.Database.ExecuteSqlRaw(GeneratedMapDataIndexPG);
                Database.ExecuteSqlRaw(MapTileIndexPG);
                Database.ExecuteSqlRaw(SlippyMapTileIndexPG);
                Database.ExecuteSqlRaw(StoredElementsIndexPG);
            }
            else //SQL Server and MariaDB share the same syntax here
            {
                //db.Database.ExecuteSqlRaw(GeneratedMapDataIndex);
                Database.ExecuteSqlRaw(MapTileIndex);
                Database.ExecuteSqlRaw(SlippyMapTileIndex);
                Database.ExecuteSqlRaw(StoredElementsIndex);
            }

            if (serverMode == "SQLServer")
            {
                Database.ExecuteSqlRaw(GeneratedMapDataValidTriggerMSSQL);
            }
            if (serverMode == "MariaDB")
            {
                Database.ExecuteSqlRaw("SET collation_server = 'utf8mb4_unicode_ci'; SET character_set_server = 'utf8mb4'"); //MariaDB defaults to latin2_swedish, we need Unicode.
            }

            InsertDefaultServerConfig();
            InsertDefaultStyle();
        }

        public void InsertDefaultServerConfig()
        {
            //var db = new PraxisContext();
            ServerSettings.Add(new ServerSetting() { id = 1, NorthBound = 90, SouthBound = -90, EastBound = 180, WestBound = -180, SlippyMapTileSizeSquare = 512 });
            SaveChanges();
        }

        public void InsertDefaultStyle()
        {
            //var db = new PraxisContext();
            //Remove any existing entries, in case I'm refreshing the rules on an existing entry.
            if (serverMode != "PostgreSQL") //PostgreSQL has stricter requirements on its syntax.
            {
                //db.Database.ExecuteSqlRaw("DELETE FROM TagParserEntriesTagParserMatchRules");
                //db.Database.ExecuteSqlRaw("DELETE FROM TagParserEntries");
                //db.Database.ExecuteSqlRaw("DELETE FROM TagParserMatchRules");
            }

            if (serverMode == "SQLServer")
            {
                Database.BeginTransaction();
                Database.ExecuteSqlRaw("SET IDENTITY_INSERT TagParserEntries ON;");
            }
            TagParserEntries.AddRange(Singletons.defaultTagParserEntries);
            SaveChanges();
            if (serverMode == "SQLServer")
            {
                Database.ExecuteSqlRaw("SET IDENTITY_INSERT TagParserEntries OFF;");
                Database.CommitTransaction();
            }

            foreach (var file in System.IO.Directory.EnumerateFiles("MapPatterns"))
            {
                TagParserStyleBitmaps.Add(new TagParserStyleBitmap() {
                    filename = System.IO.Path.GetFileName(file),
                    data = System.IO.File.ReadAllBytes(file)
                });
            }
            SaveChanges();
        }

        public void RecreateIndexes()
        {
            //Only run this after running DropIndexes, since these should all exist on DB creation.
            
            //PostgreSQL will make automatic spatial indexes
            if (serverMode == "PostgreSQL")
            {
                //db.Database.ExecuteSqlRaw(GeneratedMapDataIndexPG);
                Database.ExecuteSqlRaw(MapTileIndexPG);
                Database.ExecuteSqlRaw(SlippyMapTileIndexPG);
                Database.ExecuteSqlRaw(StoredElementsIndexPG);
            }
            else //SQL Server and MariaDB share the same syntax here
            {
                //db.Database.ExecuteSqlRaw(GeneratedMapDataIndex);
                Database.ExecuteSqlRaw(MapTileIndex);
                Database.ExecuteSqlRaw(SlippyMapTileIndex);
                Database.ExecuteSqlRaw(StoredElementsIndex);

                //now also add the automatic ones we took out in DropIndexes.
                Database.ExecuteSqlRaw(areaSizeIndex);
                Database.ExecuteSqlRaw(sourceItemIdIndex);
                Database.ExecuteSqlRaw(sourceItemTypeIndex);
                Database.ExecuteSqlRaw(tagKeyIndex);
                Database.ExecuteSqlRaw(privacyIdIndex);
                Database.ExecuteSqlRaw(customDataPlusCodesIndex);
            }
        }

        public void DropIndexes()
        {
            Database.ExecuteSqlRaw(DropMapTileIndex);
            Database.ExecuteSqlRaw(DropStoredElementsIndex);
            Database.ExecuteSqlRaw(DropSlippyMapTileIndex);
            Database.ExecuteSqlRaw(DropcustomDataPlusCodesIndex);
            Database.ExecuteSqlRaw(DropStoredElementsAreaSizeIndex);
            Database.ExecuteSqlRaw(DropStoredElementsPrivacyIdIndex);
            Database.ExecuteSqlRaw(DropStoredElementsSourceItemTypeIndex);
            Database.ExecuteSqlRaw(DropStoredElementsSourceItemIdIndex);
            Database.ExecuteSqlRaw(DropTagKeyIndex);
        }

        /// <summary>
        /// Force gameplay maptiles to expire and be redrawn on next access. Can be limited to a specific style set or run on all tiles.
        /// </summary>
        /// <param name="g">the area to expire intersecting maptiles with</param>
        /// <param name="styleSet">which set of maptiles to expire. All tiles if this is an empty string</param>
        public void ExpireMapTiles(Geometry g, string styleSet = "")
        {
            //If this would be faster as raw SQL, see function below for a template on how to write that.
            //TODO: test this logic, should be faster but 
            //var db = new PraxisContext();
            //MariaDB SQL, should be functional
            string SQL = "UPDATE MapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE (styleSet= '" + styleSet + "' OR '" + styleSet + "' = '') AND ST_INTERSECTS(areaCovered, ST_GEOMFROMTEXT('" + g.AsText() + "'))";
            Database.ExecuteSqlRaw(SQL);
        }

        /// <summary>
        /// Force gameplay maptiles to expire and be redrawn on next access. Can be limited to a specific style set or run on all tiles.
        /// </summary>
        /// <param name="elementId">the privacyID of a storedOsmElement to expire intersecting tiles for.</param>
        /// <param name="styleSet">which set of maptiles to expire. All tiles if this is an empty string</param>
        public void ExpireMapTiles(Guid elementId, string styleSet = "")
        {
            //If this would be faster as raw SQL, see function below for a template on how to write that.
            //TODO: test this logic, should be faster but 
            //var db = new PraxisContext();
            //MariaDB SQL, should be functional
            string SQL = "UPDATE MapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE (styleSet= '" + styleSet + "' OR '" + styleSet + "' = '') AND ST_INTERSECTS(areaCovered, (SELECT elementGeometry FROM StoredOsmElements WHERE privacyId = '" + elementId + "'))";
            Database.ExecuteSqlRaw(SQL);
        }

        /// <summary>
        /// Force SlippyMap tiles to expire and be redrawn on next access. Can be limited to a specific style set or run on all tiles.
        /// </summary>
        /// <param name="g">the area to expire intersecting maptiles with</param>
        /// <param name="styleSet">which set of SlippyMap tiles to expire. All tiles if this is an empty string</param>
        public void ExpireSlippyMapTiles(Geometry g, string styleSet = "")
        {
            //If this would be faster as raw SQL, see function below for a template on how to write that.
            //TODO: test this logic, should be faster but 
            //var db = new PraxisContext();
            //MariaDB SQL, should be functional
            string SQL = "UPDATE SlippyMapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE (styleSet= '" + styleSet + "' OR '" + styleSet + "' = '') AND ST_INTERSECTS(areaCovered, ST_GEOMFROMTEXT('" + g.AsText() + "'))";
            Database.ExecuteSqlRaw(SQL);
        }

        /// <summary>
        /// Force SlippyMap tiles to expire and be redrawn on next access. Can be limited to a specific style set or run on all tiles.
        /// </summary>
        /// <param name="elementId">the privacyID of a storedOsmElement to expire intersecting tiles for.</param>
        /// <param name="styleSet">which set of maptiles to expire. All tiles if this is an empty string</param>
        public void ExpireSlippyMapTiles(Guid elementId, string styleSet = "")
        {
            //Might this be better off as raw SQL? If I expire, say, an entire state, that could be a lot of map tiles to pull into RAM just for a date to change.
            //var raw = "UPDATE SlippyMapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE ST_INTERSECTS(areaCovered, ST_GeomFromText(" + g.AsText() + "))";
            //var db = new PraxisContext();
            string SQL = "UPDATE SlippyMapTiles SET ExpireOn = CURRENT_TIMESTAMP WHERE (styleSet = '" + styleSet + "' OR '" + styleSet + "' = '') AND ST_INTERSECTS(areaCovered, (SELECT elementGeometry FROM StoredOsmElements WHERE privacyId = '" + elementId + "'))";
            Database.ExecuteSqlRaw(SQL);
        }
    }
}

