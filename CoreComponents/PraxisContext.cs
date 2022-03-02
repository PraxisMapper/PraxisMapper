using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using System;
using System.Collections.Generic;
using System.Linq;
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
        public DbSet<DbTables.Place> Places { get; set; }
        public DbSet<StyleEntry> StyleEntries { get; set; }
        public DbSet<StyleMatchRule> StyleMatchRules { get; set; }
        public DbSet<StylePaint> StylePaints { get; set; }
        public DbSet<PlaceTags> PlaceTags { get; set; } 
        public DbSet<PlaceGameData> PlaceGameData { get; set; }
        public DbSet<AreaGameData> AreaGameData { get; set; }
        public DbSet<GlobalDataEntries> GlobalDataEntries { get; set; }
        public DbSet<StyleBitmap> TagParserStyleBitmaps { get; set; }

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
            model.Entity<PlayerData>().HasIndex(p => p.DeviceID);
            model.Entity<PlayerData>().HasIndex(p => p.DataKey);

            model.Entity<DbTables.Place>().HasIndex(m => m.AreaSize); //Enables server-side sorting on biggest-to-smallest draw order.
            model.Entity<DbTables.Place>().HasIndex(m => m.SourceItemID);
            model.Entity<DbTables.Place>().HasIndex(m => m.SourceItemType);
            model.Entity<DbTables.Place>().HasIndex(m => m.PrivacyId);
            model.Entity<DbTables.Place>().HasMany(m => m.Tags).WithOne(m => m.Place).HasForeignKey(m => (new { m.SourceItemId, m.SourceItemType })).HasPrincipalKey(m => (new { m.SourceItemID, m.SourceItemType }));

            model.Entity<MapTile>().HasIndex(m => m.PlusCode);
            model.Entity<MapTile>().Property(m => m.PlusCode).HasMaxLength(12);
            model.Entity<MapTile>().HasIndex(m => m.StyleSet);

            model.Entity<SlippyMapTile>().HasIndex(m => m.Values);
            model.Entity<SlippyMapTile>().HasIndex(m => m.StyleSet);

            model.Entity<PlaceTags>().HasIndex(m => m.Key);
            model.Entity<PlaceTags>().HasOne(m => m.Place).WithMany(m => m.Tags).HasForeignKey(m => new { m.SourceItemId, m.SourceItemType }).HasPrincipalKey(m => new { m.SourceItemID, m.SourceItemType });

            model.Entity<PlaceGameData>().HasIndex(m => m.DataKey);
            model.Entity<PlaceGameData>().HasIndex(m => m.PlaceId);

            model.Entity<AreaGameData>().HasIndex(m => m.DataKey);
            model.Entity<AreaGameData>().HasIndex(m => m.PlusCode);
            model.Entity<AreaGameData>().HasIndex(m => m.GeoAreaIndex);

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
        public static string StoredElementsIndex = "CREATE SPATIAL INDEX PlacesIndex ON Places(elementGeometry)";
        public static string customDataPlusCodesIndex = "CREATE SPATIAL INDEX areaGameDataSpatialIndex ON AreaGameData(geoAreaIndex)";
        public static string areaSizeIndex = "CREATE OR REPLACE INDEX IX_Places_AreaSize on Places (AreaSize)";
        public static string privacyIdIndex = "CREATE OR REPLACE INDEX IX_Places_privacyId on Places (privacyId)";
        public static string sourceItemIdIndex = "CREATE OR REPLACE INDEX IX_Places_sourceItemID on Places (sourceItemID)";
        public static string sourceItemTypeIndex = "CREATE OR REPLACE INDEX IX_Places_sourceItemType on Places (sourceItemType)";
        public static string tagKeyIndex = "CREATE OR REPLACE INDEX IX_PlaceTags_Key on PlaceTags (`Key`)";

        //PostgreSQL uses its own CREATE INDEX syntax
        public static string MapTileIndexPG = "CREATE INDEX maptiles_geom_idx ON public.\"MapTiles\" USING GIST(\"areaCovered\")";
        public static string SlippyMapTileIndexPG = "CREATE INDEX slippmayptiles_geom_idx ON public.\"SlippyMapTiles\" USING GIST(\"areaCovered\")";
        public static string StoredElementsIndexPG = "CREATE INDEX storedOsmElements_geom_idx ON public.\"Places\" USING GIST(\"elementGeometry\")";

        //Adding these as helper values for large use cases. When inserting large amounts of data, it's probably worth removing indexes for the insert and re-adding them later.
        //(On a North-America file insert, this keeps insert speeds at about 2-3 seconds per block, whereas it creeps up consistently while indexes are updated per block.
        //Though, I also see better results there droping the single-column indexes as well, which would need re-created manually since those one are automatic.
        public static string DropMapTileIndex = "DROP INDEX IF EXISTS MapTileSpatialIndex on MapTiles";
        public static string DropSlippyMapTileIndex = "DROP INDEX IF EXISTS SlippyMapTileSpatialIndex on SlippyMapTiles";
        public static string DropStoredElementsIndex = "DROP INDEX IF EXISTS PlacesIndex on Places";
        public static string DropcustomDataPlusCodesIndex = "DROP INDEX IF EXISTS areaGameDataSpatialIndex on AreaGameData";
        public static string DropStoredElementsAreaSizeIndex = "DROP INDEX IF EXISTS IX_Places_AreaSize on Places";
        public static string DropStoredElementsPrivacyIdIndex = "DROP INDEX IF EXISTS IX_Places_privacyId on Places";
        public static string DropStoredElementsSourceItemIdIndex = "DROP INDEX IF EXISTS IX_Places_sourceItemID on Places";
        public static string DropStoredElementsSourceItemTypeIndex = "DROP INDEX IF EXISTS IX_Places_sourceItemType on Places";
        public static string DropTagKeyIndex = "DROP INDEX IF EXISTS IX_PlaceTags_Key on PlaceTags";

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
            ServerSettings.Add(new ServerSetting() { Id = 1, NorthBound = 90, SouthBound = -90, EastBound = 180, WestBound = -180, SlippyMapTileSizeSquare = 512 });
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
                Database.ExecuteSqlRaw("SET IDENTITY_INSERT StyleEntries ON;");
            }
            StyleEntries.AddRange(Singletons.defaultStyleEntries);
            SaveChanges();
            if (serverMode == "SQLServer")
            {
                Database.ExecuteSqlRaw("SET IDENTITY_INSERT StyleEntries OFF;");
                Database.CommitTransaction();
            }

            foreach (var file in System.IO.Directory.EnumerateFiles("MapPatterns"))
            {
                TagParserStyleBitmaps.Add(new StyleBitmap()
                {
                    Filename = System.IO.Path.GetFileName(file),
                    Data = System.IO.File.ReadAllBytes(file)
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

        public GeoArea SetServerBounds(long singleArea)
        {
            //This is an important command if you don't want to track data outside of your initial area.
            GeoArea results = null;
            if (singleArea != 0)
            {
                var area = Places.First(e => e.SourceItemID == singleArea);
                var envelop = area.ElementGeometry.EnvelopeInternal;
                results = new GeoArea(envelop.MinY, envelop.MinX, envelop.MaxY, envelop.MaxX);
            }
            else
                results = Place.DetectServerBounds(ConstantValues.resolutionCell8);

            var settings = ServerSettings.FirstOrDefault();
            settings.NorthBound = results.NorthLatitude;
            settings.SouthBound = results.SouthLatitude;
            settings.EastBound = results.EastLongitude;
            settings.WestBound = results.WestLongitude;
            SaveChanges();
            return results;
        }

        public void UpdateExistingEntries(List<DbTables.Place> entries)
        {
            foreach (var entry in entries)
            {
                //check existing entry, see if it requires being updated
                var existingData = Places.FirstOrDefault(md => md.SourceItemID == entry.SourceItemID && md.SourceItemType == entry.SourceItemType);
                if (existingData != null)
                {
                    if (existingData.AreaSize != entry.AreaSize) existingData.AreaSize = entry.AreaSize;
                    if (existingData.GameElementName != entry.GameElementName) existingData.GameElementName = entry.GameElementName;

                    bool expireTiles = false;
                    if (!existingData.ElementGeometry.EqualsTopologically(entry.ElementGeometry)) //TODO: this might need to be EqualsExact?
                    {
                        //update the geometry for this object.
                        existingData.ElementGeometry = entry.ElementGeometry;
                        expireTiles = true;
                    }
                    if (!existingData.Tags.OrderBy(t => t.Key).SequenceEqual(entry.Tags.OrderBy(t => t.Key)))
                    {
                        existingData.Tags = entry.Tags;
                        var styleA = TagParser.GetStyleForOsmWay(existingData.Tags);
                        var styleB = TagParser.GetStyleForOsmWay(entry.Tags);
                        if (styleA != styleB)
                            expireTiles = true; //don't force a redraw on tags unless we change our drawing rules.
                    }

                    if (expireTiles) //geometry or style has to change, otherwise we can skip expiring values.
                    {
                        SaveChanges(); //save before expiring, so the next redraw absolutely has the latest data. Can't catch it mid-command if we do this first.
                        ExpireMapTiles(entry.ElementGeometry, "mapTiles");
                        ExpireSlippyMapTiles(entry.ElementGeometry, "mapTiles");
                    }
                }
                else
                {
                    //We don't have this item, add it.
                    Places.Add(entry);
                    SaveChanges(); //again, necessary here to get tiles to draw correctly after expiring.
                    ExpireMapTiles(entry.ElementGeometry, "mapTiles");
                    ExpireSlippyMapTiles(entry.ElementGeometry, "mapTiles");
                }
            }
            SaveChanges(); //final one for anything not yet persisted.
        }

        public void ResetStyles()
        {
            Log.WriteLog("Replacing current styles with default ones");
            var styles = Singletons.defaultStyleEntries.Select(t => t.StyleSet).Distinct().ToList();

            var toRemove = StyleEntries.Include(t => t.PaintOperations).Where(t => styles.Contains(t.StyleSet)).ToList();
            var toRemovePaints = toRemove.SelectMany(t => t.PaintOperations).ToList();
            var toRemoveImages = TagParserStyleBitmaps.ToList();
            StylePaints.RemoveRange(toRemovePaints);
            SaveChanges();
            StyleEntries.RemoveRange(toRemove);
            SaveChanges();
            TagParserStyleBitmaps.RemoveRange(toRemoveImages);
            SaveChanges();

            InsertDefaultStyle();
            Log.WriteLog("Styles restored to PraxisMapper defaults");
        }
    }
}