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
            var row = db.customDataOsmElements.Where(p => p.storedOsmElementId == elementId && p.dataKey == key).FirstOrDefault();
            if (row == null)
            {
                row = new DbTables.CustomDataOsmElement();
                row.dataKey = key;
                row.storedOsmElementId = elementId;
            }
            if(expiration != null)
                row.expiration = expiration;
            row.dataValue = value;
            return db.SaveChanges() == 1;
        }

        public static string GetElementData(long elementId, string key)
        {
            var db = new PraxisContext();
            var row = db.customDataOsmElements.Where(p => p.storedOsmElementId == elementId && p.dataKey == key).FirstOrDefault();
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
            }
            if (expiration != null)
                row.expiration = expiration;
            row.dataValue = value;
            return db.SaveChanges() == 1;
        }
    }
}
