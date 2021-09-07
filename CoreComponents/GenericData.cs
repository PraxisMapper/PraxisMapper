using CoreComponents.Support;
using Google.OpenLocationCode;

namespace CoreComponents
{
    //Handles read/write for the generic area/player data tables.
    //TODO: set up existing games to use this structure instead of their own hard-coded serverside bits.
    public static class GenericData
    {
        public static bool SetPlusCodeData(string plusCode, string key, string value, DateTime? expiration = null)
        {
            var db = new PraxisContext();
            //An upsert command would be great here, but I dont think the entities do that.
            var row = db.CustomDataPlusCodes.Where(p => p.PlusCode == plusCode && p.dataKey == key).FirstOrDefault();
            if (row == null)
            {
                row = new DbTables.CustomDataPlusCode();
                row.dataKey = key;
                row.PlusCode = plusCode;
                row.geoAreaIndex = Converters.GeoAreaToPolygon(OpenLocationCode.DecodeValid(plusCode.ToUpper()));
                db.CustomDataPlusCodes.Add(row);
            }
            if (expiration != null)
                row.expiration = expiration;
            row.dataValue = value;
            return db.SaveChanges() == 1;
        }

        public static string GetPlusCodeData(string plusCode, string key)
        {
            var db = new PraxisContext();
            var row = db.CustomDataPlusCodes.Where(p => p.PlusCode == plusCode && p.dataKey == key).FirstOrDefault();
            if (row == null || row.expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.Now)
                return "";
            return row.dataValue;
        }

        public static bool SetStoredElementData(long elementId, string key, string value, DateTime? expiration = null)
        {
            var db = new PraxisContext();
            //An upsert command would be great here, but I dont think the entities do that.
            var row = db.CustomDataOsmElements.Where(p => p.StoredOsmElementId == elementId && p.dataKey == key).FirstOrDefault();
            if (row == null)
            {
                row = new DbTables.CustomDataOsmElement();
                row.dataKey = key;
                row.StoredOsmElementId = elementId;
                db.CustomDataOsmElements.Add(row);
            }
            if(expiration != null)
                row.expiration = expiration;
            row.dataValue = value;
            return db.SaveChanges() == 1;
        }

        public static string GetElementData(long elementId, string key)
        {
            var db = new PraxisContext();
            var row = db.CustomDataOsmElements.Where(p => p.StoredOsmElementId == elementId && p.dataKey == key).FirstOrDefault();
            if (row == null || row.expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.Now)
                return "";
            return row.dataValue;
        }

        public static string GetPlayerData(string playerId, string key)
        {
            var db = new PraxisContext();
            var row = db.PlayerData.Where(p => p.deviceID == playerId && p.dataKey == key).FirstOrDefault();
            if (row == null || row.expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.Now)
                return "";
            return row.dataValue;
        }

        public static bool SetPlayerData(string playerId, string key, string value, DateTime? expiration = null)
        {
            var db = new PraxisContext();
            //An upsert command would be great here, but I dont think the entities do that.
            var row = db.PlayerData.Where(p => p.deviceID == playerId && p.dataKey == key).FirstOrDefault();
            if (row == null)
            {
                row = new DbTables.PlayerData();
                row.dataKey = key;
                row.deviceID = playerId;
                db.PlayerData.Add(row);
            }
            if (expiration != null)
                row.expiration = expiration;
            row.dataValue = value;
            return db.SaveChanges() == 1;
        }

        //NOTE: i thought about having both functions return both tables's worth of data merged, but I think
        //for performance reasons it'll be better to keep them separate and allow the actual game to do 
        //the merging logic in those cases. Most data sets will be one or the other.
        public static List<CustomDataResult> GetAllDataInPlusCode(string plusCode) //TODO: add optional key filter
        {
            var db = new PraxisContext();
            var plusCodeArea = OpenLocationCode.DecodeValid(plusCode);
            var plusCodePoly = Converters.GeoAreaToPolygon(plusCodeArea);
            var plusCodeData = db.CustomDataPlusCodes.Where(d => plusCodePoly.Intersects(d.geoAreaIndex)).Select(d => new CustomDataResult(d.PlusCode, d.dataKey, d.dataValue)).ToList();

            return plusCodeData;
        }

        public static List<CustomDataResult> GetAllPlusCodeDataInArea(GeoArea area) //TODO: add optional key filter
        {
            var db = new PraxisContext();
            var poly = Converters.GeoAreaToPolygon(area);
            var plusCodeData = db.CustomDataPlusCodes.Where(d => poly.Intersects(d.geoAreaIndex)).Select(d => new CustomDataResult(d.PlusCode, d.dataKey, d.dataValue)).ToList();

            return plusCodeData;
        }

        public static List<CustomDataAreaResult> GetAllDataInArea(GeoArea area)
        {
            var db = new PraxisContext();
            var poly = Converters.GeoAreaToPolygon(area);
            var data = db.CustomDataOsmElements.Where(d => poly.Intersects(d.storedOsmElement.elementGeometry)).Select(d => new CustomDataAreaResult(d.StoredOsmElementId, d.dataKey, d.dataValue)).ToList();

            return data;
        }

        public static List<CustomDataAreaResult> GetAllDataInPlace(long elementId, int elementType)
        {
            var db = new PraxisContext();
            var place = db.StoredOsmElements.Where(s => s.id == elementId && s.sourceItemType == elementType).First();
            var data = db.CustomDataOsmElements.Where(d => place.elementGeometry.Intersects(d.storedOsmElement.elementGeometry)).Select(d => new CustomDataAreaResult(d.StoredOsmElementId, d.dataKey, d.dataValue)).ToList();

            return data;
        }

        public static string GetGlobalData(string key)
        {
            var db = new PraxisContext();
            var row = db.GlobalDataEntries.Where(s => s.dataKey == key).FirstOrDefault();
            if (row == null)
                return "";

            return row.dataValue;
        }

        public static bool SetGlobalData(string key, string value)
        {
            var db = new PraxisContext();
            var row = db.GlobalDataEntries.Where(p => p.dataKey == key).FirstOrDefault();
            if (row == null)
            {
                row = new DbTables.GlobalDataEntries();
                row.dataKey = key;
                db.GlobalDataEntries.Add(row);
            }
            row.dataValue = value;
            return db.SaveChanges() == 1;
        }
    }
}
