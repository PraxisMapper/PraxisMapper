using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using static PraxisCore.DbTables;

namespace PraxisCore
{
    /// <summary>
    /// Classes that handle reading and writing key-value pairs for players, places, pluscodes, or the global set.
    /// </summary>
    public static class GenericData
    {
        static Aes baseSec = Aes.Create();
        public static bool enableCaching = false;
        public static IMemoryCache memoryCache = null;

        /// <summary>
        /// Saves a key/value pair to a given PlusCode. Will reject a pair containing a player's accountId in the database.
        /// </summary>
        /// <param name="plusCode">A valid PlusCode, excluding the + symbol.</param>
        /// <param name="key">The key to save to the database. Keys are unique, and you cannot have multiples of the same key.</param>
        /// <param name="value">The value to save with the key.</param>
        /// <param name="expiration">If not null, expire this data in this many seconds from now.</param>
        /// <returns>true if data was saved, false if data was not.</returns>
        public static bool SetAreaData(string plusCode, string key, string value, double? expiration = null)
        {
            return SetAreaData(plusCode, key, value.ToByteArrayUTF8(), expiration);
        }


        /// <summary>
        /// Saves a key/value pair to a given PlusCode, with the value being converted to JSON text in a byte[]. Will reject a pair containing a player's accountId in the database.
        /// </summary>
        public static bool SetAreaDataJson(string plusCode, string key, object value, double? expiration = null)
        {
            return SetAreaData(plusCode, key, value.ToJsonByteArray(), expiration);
        }

        /// <summary>
        /// Saves a key/value pair to a given PlusCode, with a value provided as a byte[]. Will reject a pair containing a player's accountId in the database.
        /// </summary>
        public static bool SetAreaData(string plusCode, string key, byte[] value, double? expiration = null)
        {
            using var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            if (db.PlayerData.Any(p => p.accountId == key))
                return false;

            if (value.Length < 128)
            {
                string valString = value.ToUTF8String();
                if (db.PlayerData.Any(p => p.accountId == valString))
                    return false;
            }

            var row = db.AreaData.FirstOrDefault(p => p.PlusCode == plusCode && p.DataKey == key);
            if (row == null)
            {
                row = new DbTables.AreaData();
                row.DataKey = key;
                row.PlusCode = plusCode;
                row.AreaCovered = plusCode.ToPolygon();
                db.AreaData.Add(row);
            }
            else
                db.Entry(row).State = EntityState.Modified;

            if (expiration.HasValue)
                row.Expiration = DateTime.UtcNow.AddSeconds(expiration.Value);
            else
                row.Expiration = null;
            row.IvData = null;
            row.DataValue = value;
            db.SaveChanges();

            if (enableCaching && memoryCache != null)
            {
                memoryCache.Set(plusCode + "-" + key, value, new DateTimeOffset(expiration == null ?  DateTime.UtcNow.AddMinutes(15) : DateTime.UtcNow.AddSeconds(expiration.Value) ));
            }

            return true;
        }

        /// <summary>
        /// Get the value from a key/value pair saved on a PlusCode. Expired values will not be sent over.
        /// </summary>
        /// <param name="plusCode">A valid PlusCode, excluding the + symbol.</param>
        /// <param name="key">The key to load from the database on the PlusCode</param>
        /// <returns>The value saved to the key, or an empty byte[] if no key/value pair was found.</returns>
        public static byte[] GetAreaData(string plusCode, string key)
        {
            if (enableCaching && memoryCache != null)
            {
                if (memoryCache.TryGetValue(plusCode + "-" + key, out byte[] results))
                    return results;
            }

            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var row = db.AreaData.FirstOrDefault(p => p.PlusCode == plusCode && p.DataKey == key);
            if (row == null || row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.UtcNow)
                return Array.Empty<byte>();
            return row.DataValue;
        }

        /// <summary>
        /// Get the value from a key/value pair saved on a PlusCode, and casts the value from a byte[] to its JSON text form to its original type. Expired values will not be sent over.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="plusCode"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public static T GetAreaData<T>(string plusCode, string key)
        {
            return GetAreaData(plusCode, key).FromJsonBytesTo<T>();
        }

        /// <summary>
        /// Saves a key/value string pair to a given Place. Will reject a pair containing a player's accountId in the database.
        /// </summary>
        /// <param name="elementId">the Guid exposed to clients to identify the map element.</param>
        /// <param name="key">The key to save to the database for the map element.</param>
        /// <param name="value">The value to save with the key.</param>
        /// <param name="expiration">If not null, expire the data in this many seconds from now.</param>
        /// <returns>true if data was saved, false if data was not.</returns>
        public static bool SetPlaceData(Guid elementId, string key, string value, double? expiration = null)
        {
            return SetPlaceData(elementId, key, value.ToByteArrayUTF8(), expiration);
        }

        /// <summary>
        /// Saves a key/value pair to a given Place, converting the value to a byte[] of JSON text. Will reject a pair containing a player's accountId in the database.
        /// </summary>
        /// <param name="elementId"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="expiration"></param>
        /// <returns></returns>
        public static bool SetPlaceDataJson(Guid elementId, string key, object value, double? expiration = null)
        {
            return SetPlaceData(elementId, key, value.ToJsonByteArray(), expiration);
        }

        /// <summary>
        /// Saves a key/value pair to a given Place, given the object in byte[] form. Will reject a pair containing a player's accountId in the database.
        /// </summary>
        /// <param name="elementId"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="expiration"></param>
        /// <returns></returns>
        public static bool SetPlaceData(Guid elementId, string key, byte[] value, double? expiration = null)
        {
            using var db = new PraxisContext();
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                if (db.PlayerData.Any(p => p.accountId == key))
                    return false;

                var row = db.PlaceData.Include(p => p.Place).FirstOrDefault(p => p.Place.PrivacyId == elementId && p.DataKey == key);
                if (row == null)
                {
                    var sourceItem = db.Places.First(p => p.PrivacyId == elementId);
                    row = new DbTables.PlaceData();
                    row.DataKey = key;
                    row.Place = sourceItem;
                    db.PlaceData.Add(row);
                }
                else
                    db.Entry(row).State = EntityState.Modified;

                if (expiration.HasValue)
                    row.Expiration = DateTime.UtcNow.AddSeconds(expiration.Value);
                else
                    row.Expiration = null;
                row.IvData = null;
                row.DataValue = value;
            var saved = db.SaveChanges();

            if (enableCaching && memoryCache != null)
            {
                memoryCache.Set(elementId.ToString() + "-" + key, value, new DateTimeOffset(expiration == null ? DateTime.UtcNow.AddMinutes(15) : DateTime.UtcNow.AddSeconds(expiration.Value)));
            }
            return saved == 1;
        }

        /// <summary>
        /// Get the value from a key/value pair saved on a Place. Expired entries will be ignored.
        /// </summary>
        /// <param name="elementId">the Guid exposed to clients to identify the map element.</param>
        /// <param name="key">The key to load from the database. Keys are unique, and you cannot have multiples of the same key.</param>
        /// <returns>The value saved to the key, or an empty byte[] if no key/value pair was found.</returns>
        public static byte[] GetPlaceData(Guid elementId, string key)
        {
            if (enableCaching && memoryCache != null)
            {
                if (memoryCache.TryGetValue(elementId.ToString() + "-" + key, out byte[] results))
                    return results;
            }

            using var db = new PraxisContext();
                db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                var row = db.PlaceData.Include(p => p.Place).FirstOrDefault(p => p.Place.PrivacyId == elementId && p.DataKey == key);
                if (row == null || row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.UtcNow)
                    return Array.Empty<byte>();
                return row.DataValue;
        }

        /// <summary>
        /// Get the value from a key/value pair saved on a Place, cast back to the requested type. Expired entries will be ignored.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="elementId"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public static T GetPlaceData<T>(Guid elementId, string key)
        {
            return GetPlaceData(elementId, key).FromJsonBytesTo<T>();
        }

        /// <summary>
        /// Get the value from a key/value pair saved on a player's accountId. Expired entries will be ignored.
        /// </summary>
        /// <param name="accountId">the player-specific ID used. Expected to be a unique accountID to identify a phone, per that device's rules.</param>
        /// <param name="key">The key to load data from for the playerId.</param>
        /// <returns>The value saved to the key, or an empty byte[] if no key/value pair was found.</returns>
        public static byte[] GetPlayerData(string accountId, string key)
        {
            if (enableCaching && memoryCache != null)
            {
                if (memoryCache.TryGetValue(accountId + "-" + key, out byte[] results))
                    return results;
            }

            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var row = db.PlayerData.FirstOrDefault(p => p.accountId == accountId && p.DataKey == key);
            if (row == null || row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.UtcNow)
                return Array.Empty<byte>();
            return row.DataValue;
        }

        /// <summary>
        /// Get the value from a key/value pair saved on a player's accountId, cast back to the requested type. Expired entries will be ignored.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="accountId"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public static T GetPlayerData<T>(string accountId, string key)
        {
            return GetPlayerData(accountId, key).FromJsonBytesTo<T>();
        }

        /// <summary>
        /// Saves a key/value pair to a given player's accountID. Will reject a pair containing a PlusCode.
        /// </summary>
        /// <param name="accountId"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="expiration"></param>
        /// <returns></returns>
        public static bool SetPlayerData(string accountId, string key, string value, double? expiration = null)
        {
            if (DataCheck.IsPlusCode(value)) //reject attaching Player to Area
                return false;

            return SetPlayerData(accountId, key, value.ToByteArrayUTF8(), expiration);
        }


        /// <summary>
        /// Saves a key/value pair to a given player's accountID, converting the value to JSON text then a byte[]. Will reject a pair containing a PlusCode or map element Id.
        /// </summary>
        /// <param name="accountId"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="expiration"></param>
        /// <returns></returns>
        public static bool SetPlayerDataJson(string accountId, string key, object value, double? expiration = null)
        {
            return SetPlayerData(accountId, key, value.ToJsonByteArray(), expiration);
        }

        /// <summary>
        /// Saves a key/value pair to a given player's accountID. Will reject a pair containing a PlusCode or map element Id.
        /// </summary>
        /// <param name="accountId"></param>
        /// <param name="key">The key to save to the database. Keys are unique, and you cannot have multiples of the same key.</param>
        /// <param name="value">The value to save with the key.</param>
        /// <param name="expiration">If not null, expire this data in this many seconds from now.</param>
        /// <returns>true if data was saved, false if data was not.</returns>
        public static bool SetPlayerData(string accountId, string key, byte[] value, double? expiration = null)
        {
            if (string.IsNullOrWhiteSpace(accountId)) //reject saving player data without a player id
                return false;

            if (DataCheck.IsPlusCode(key)) //reject attaching Player to Area
                return false;

            var guidString = value.ToUTF8String();
            using var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            Guid tempCheck = new Guid();
            if ((Guid.TryParse(key, out tempCheck) && db.Places.Any(osm => osm.PrivacyId == tempCheck))
                || (Guid.TryParse(guidString, out tempCheck) && db.Places.Any(osm => osm.PrivacyId == tempCheck)))
                return false; //reject attaching a player to a Place

            var row = db.PlayerData.FirstOrDefault(p => p.accountId == accountId && p.DataKey == key);
            if (row == null)
            {
                row = new DbTables.PlayerData();
                row.DataKey = key;
                row.accountId = accountId;
                db.PlayerData.Add(row);
            }
            else
                db.Entry(row).State = EntityState.Modified;
            if (expiration.HasValue)
                row.Expiration = DateTime.UtcNow.AddSeconds(expiration.Value);
            else
                row.Expiration = null;
            row.IvData = null;
            row.DataValue = value;

            if (enableCaching && memoryCache != null)
            {
                memoryCache.Set(accountId + "-" + key, value, new DateTimeOffset(expiration == null ? DateTime.UtcNow.AddMinutes(15) : DateTime.UtcNow.AddSeconds(expiration.Value)));
            }

            return db.SaveChanges() == 1;
        }

        /// <summary>
        /// Load all of the key/value pairs in a PlusCode, including pairs saved to a longer PlusCode. Expired and encrypted entries are ignored.
        /// (EX: calling this with an 8 character PlusCode will load all contained 10 character PlusCodes key/value pairs)
        /// </summary>
        /// <param name="plusCode">A valid PlusCode, excluding the + symbol.</param>
        /// <param name="key">If supplied, only returns data on the given key for the area provided. If blank, returns all keys</param>
        /// <returns>a List of results with the PlusCode, keys, and values</returns>
        public static List<AreaData> GetAllDataInArea(string plusCode, string key = "")
        {
            return GetAllDataInArea(plusCode.ToGeoArea(), key);
        }

        /// <summary>
        /// Load all of the key/value pairs in a GeoArea, including pairs saved to overlapped or intersecting areas. Expired and encrypted entries are ignored.
        /// </summary>
        /// <param name="area"></param>
        /// <param name="key"></param>
        /// <returns></returns>
        public static List<AreaData> GetAllDataInArea(GeoArea area, string key = "")
        {
            //Not getting cache support: no way to expire this data directly.
            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var poly = area.ToPolygon();
            List<AreaData> data = new List<AreaData>();
            if (PraxisContext.serverMode == "MariaDB")
            {
                //NOTE: as long as https://jira.mariadb.org/browse/MDEV-26123 remains open, I have to do this workaround for MariaDB.
                data = db.AreaData.Where(d => poly.Contains(d.AreaCovered)).ToList();
                data = data.Where(d => (key == "" || key == d.DataKey) && d.IvData == null && (d.Expiration == null || d.Expiration > DateTime.UtcNow)).ToList();
            }
            else
                data = db.AreaData.Where(d => poly.Contains(d.AreaCovered) && (key == "" || key == d.DataKey) && d.IvData == null && (d.Expiration == null || d.Expiration > DateTime.UtcNow))
                    .ToList();

            return data;
        }

        /// <summary>
        /// Load all of the key/value pairs attached to a Place. Expired entries will be ignored.
        /// </summary>
        /// <param name="elementId">the Guid exposed to clients to identify the map element.</param>
        /// /// <param name="key">If supplied, only returns data on the given key for the area provided. If blank, returns all keys</param>
        /// <returns>a List of results with the map element ID, keys, and values</returns>
        public static List<PlaceData> GetAllDataInPlace(Guid elementId, string key = "")
        {
            //not getting caching support: cannot directly expire data.
            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var data = db.PlaceData.Where(d => d.Place.PrivacyId == elementId && (key == "" || d.DataKey == key) && d.IvData == null && (d.Expiration == null || d.Expiration > DateTime.UtcNow))
                .ToList();

            return data;
        }

        /// <summary>
        /// Returns all data attached to a player's account ID
        /// </summary>
        /// <param name="accountID">the device associated with a player</param>
        /// <returns>List of results with accountId, keys, and values</returns>
        public static List<PlayerData> GetAllPlayerData(string accountID)
        {
            //Not getting caching support - cannot directly expire data.
            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var data = db.PlayerData.Where(p => p.accountId == accountID && p.IvData == null && (p.Expiration == null || p.Expiration > DateTime.UtcNow))
                .ToList();

            return data;
        }

        /// <summary>
        /// Loads a key/value pair from the database that isn't attached to anything specific. Global entries do not expire.
        /// </summary>
        /// <param name="key">The key to load data from.</param>
        /// <returns>The value saved to the key, or an empty string if no key/value pair was found.</returns>
        public static byte[] GetGlobalData(string key)
        {
            if (enableCaching && memoryCache != null)
            {
                if (memoryCache.TryGetValue("globalVal-" + key, out byte[] results))
                    return results;
            }

            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var row = db.GlobalData.FirstOrDefault(s => s.DataKey == key);
            if (row == null)
                return Array.Empty<byte>();
            return row.DataValue;
        }

        /// <summary>
        /// Loads a key/value pair from the database that isn't attached to anything specific, and casts it to the given type. Global entries do not expire.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <returns></returns>
        public static T GetGlobalData<T>(string key)
        {
            return GetGlobalData(key).FromJsonBytesTo<T>();
        }


        /// <summary>
        /// Saves a key/value pair to the database that isn't attached to anything specific. Wil reject a pair that contains a player's device ID, PlusCode, or a map element ID. Global entries cannot be set to expire.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public static bool SetGlobalData(string key, string value)
        {
            return SetGlobalData(key, value.ToByteArrayUTF8());
        }

        /// <summary>
        /// Saves a key/value pair to the database that isn't attached to anything specific, casting the object to JSON and then a byte[]. Wil reject a pair that contains a player's device ID, PlusCode, or a map element ID. Global entries cannot be set to expire.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public static bool SetGlobalDataJson(string key, object value)
        {
            return SetGlobalData(key, value.ToJsonByteArray());
        }

        /// <summary>
        /// Saves a key/value pair to the database that isn't attached to anything specific. Wil reject a pair that contains a player's device ID, PlusCode, or a map element ID. Global entries cannot be set to expire.
        /// </summary>
        /// <param name="key">The key to save to the database. Keys are unique, and you cannot have multiples of the same key.</param>
        /// <param name="value">The value to save with the key.</param>
        /// <returns>true if data was saved, false if data was not.</returns>
        public static bool SetGlobalData(string key, byte[] value)
        {
            bool trackingPlayer = false;
            bool trackingLocation = false;

            string valString = "";
            if (value.Length < 128)
                valString = value.ToUTF8String();

            using var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            if (db.PlayerData.Any(p => p.accountId == key || p.accountId == valString))
                trackingPlayer = true;

            if (DataCheck.IsPlusCode(key) || DataCheck.IsPlusCode(valString))
                trackingLocation = true;

            Guid tempCheck = new Guid();
            if ((Guid.TryParse(key, out tempCheck) && db.Places.Any(osm => osm.PrivacyId == tempCheck))
                || (Guid.TryParse(valString, out tempCheck) && db.Places.Any(osm => osm.PrivacyId == tempCheck)))
                trackingLocation = true;

            if (trackingLocation && trackingPlayer) //Do not allow players and locations to be attached on the global level as a workaround to being blocked on the individual levels.
                return false;

            var row = db.GlobalData.FirstOrDefault(p => p.DataKey == key);
            if (row == null)
            {
                row = new DbTables.GlobalData();
                row.DataKey = key;
                db.GlobalData.Add(row);
            }
            else
                db.Entry(row).State = EntityState.Modified;

            row.DataValue = value;

            if (enableCaching && memoryCache != null)
            {
                memoryCache.Set("globalVal-" + key, value, new DateTimeOffset(DateTime.UtcNow.AddMinutes(15)));
            }

            return db.SaveChanges() == 1;
        }

        /// <summary>
        /// Encrypts and saves a key/value pair to an Area in the database, casting the value to a byte[]. Required if the value contains player information.
        /// </summary>
        /// <param name="plusCode"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="password"></param>
        /// <param name="expiration"></param>
        /// <returns></returns>
        public static bool SetSecureAreaData(string plusCode, string key, string value, string password, double? expiration = null)
        {
            return SetSecureAreaData(plusCode, key, value.ToByteArrayUTF8(), password, expiration);
        }

        /// <summary>
        /// Encrypts and saves a key/value pair to an Area in the database, casting the value to JSON and then byte[]. Required if the value contains player information.
        /// </summary>
        /// <param name="plusCode"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="password"></param>
        /// <param name="expiration"></param>
        /// <returns></returns>
        public static bool SetSecureAreaDataJson(string plusCode, string key, object value, string password, double? expiration = null)
        {
            return SetSecureAreaData(plusCode, key, value.ToJsonByteArray(), password, expiration);
        }

        /// <summary>
        /// Encrypts and saves a key/value pair to an Area in the database. Required if the value contains player information.
        /// </summary>
        /// <param name="plusCode"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="password"></param>
        /// <param name="expiration"></param>
        /// <returns></returns>
        public static bool SetSecureAreaData(string plusCode, string key, byte[] value, string password, double? expiration = null)
        {
            using var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            byte[] encryptedValue = EncryptValue(value, password, out byte[] IVs);

            var row = db.AreaData.FirstOrDefault(p => p.PlusCode == plusCode && p.DataKey == key);
            if (row == null)
            {
                row = new DbTables.AreaData();
                row.DataKey = key;
                row.PlusCode = plusCode;
                row.AreaCovered = plusCode.ToPolygon();
                db.AreaData.Add(row);
            }
            else
                db.Entry(row).State = EntityState.Modified;

            if (expiration.HasValue)
                row.Expiration = DateTime.UtcNow.AddSeconds(expiration.Value);
            else
                row.Expiration = null;

            row.DataValue = encryptedValue;
            row.IvData = IVs;

            if (enableCaching && memoryCache != null)
            {
                memoryCache.Set(plusCode + "-" + key, row, new DateTimeOffset(expiration == null ? DateTime.UtcNow.AddMinutes(15) : DateTime.UtcNow.AddSeconds(expiration.Value)));
            }
            return db.SaveChanges() == 1;
        }

        /// <summary>
        /// Get the value from a key/value pair saved on an Area with a password. Expired values will not be sent over.
        /// </summary>
        /// <param name="plusCode">A valid PlusCode, excluding the + symbol.</param>
        /// <param name="key">The key to load from the database on the PlusCode</param>
        /// <param name="password">The password used to encrypt the value originally.</param>
        /// <returns>The value saved to the key, or an empty string if no key/value pair was found or the password is incorrect.</returns>
        public static byte[] GetSecureAreaData(string plusCode, string key, string password)
        {
            if (enableCaching && memoryCache != null)
            {
                if (memoryCache.TryGetValue(plusCode + "-" + key, out AreaData results))
                    return DecryptValue(results.IvData, results.DataValue, password);
            }

            using var db = new PraxisContext();
            var row = db.AreaData.FirstOrDefault(p => p.PlusCode == plusCode && p.DataKey == key);
            if (row == null || row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.UtcNow)
                return Array.Empty<byte>();

            return DecryptValue(row.IvData, row.DataValue, password);
        }

        /// <summary>
        /// Get the value from a key/value pair saved on an Area with a password and casts it to the requested type. Expired values will not be sent over.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="plusCode"></param>
        /// <param name="key"></param>
        /// <param name="password"></param>
        /// <returns></returns>
        public static T GetSecureAreaData<T>(string plusCode, string key, string password)
        {
            return GetSecureAreaData(plusCode, key, password).FromJsonBytesTo<T>();
        }

        /// <summary>
        /// Get the value from a key/value pair saved on a player's accountId encrypted with the given password. Expired entries will be ignored.
        /// </summary>
        /// <param name="accountId">the player-specific ID used. Expected to be a unique accountId to identify a player.</param>
        /// <param name="key">The key to load data from for the playerId.</param>
        /// <param name="password">The password used to encrypt the value originally.</param>
        /// <returns>The value saved to the key with the password given, or an empty string if no key/value pair was found or the password is incorrect.</returns>
        public static byte[] GetSecurePlayerData(string accountId, string key, string password)
        {
            if (enableCaching && memoryCache != null)
            {
                if (memoryCache.TryGetValue(accountId + "-" + key, out PlayerData results))
                    return DecryptValue(results.IvData, results.DataValue, password);
            }

            using var db = new PraxisContext();
            var row = db.PlayerData.FirstOrDefault(p => p.accountId == accountId && p.DataKey == key);
            if (row == null || row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.UtcNow)
                return Array.Empty<byte>();
            return DecryptValue(row.IvData, row.DataValue, password);
        }

        /// <summary>
        /// Get the value from a key/value pair saved on a player's accountId encrypted with the given password, and casts it to the requested type. Expired entries will be ignored.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="playerId"></param>
        /// <param name="key"></param>
        /// <param name="password"></param>
        /// <returns></returns>
        public static T GetSecurePlayerData<T>(string playerId, string key, string password)
        {
            return GetSecurePlayerData(playerId, key, password).FromJsonBytesTo<T>();
        }

        /// <summary>
        /// Saves the value from a key/value pair on a player's accountId encrypted with the given password. 
        /// </summary>
        /// <param name="playerId"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="password"></param>
        /// <param name="expiration"></param>
        /// <returns></returns>
        public static bool SetSecurePlayerData(string playerId, string key, string value, string password, double? expiration = null)
        {
            return SetSecurePlayerData(playerId, key, value.ToByteArrayUTF8(), password, expiration);
        }

        /// <summary>
        /// Saves the value from a key/value pair on a player's accountId encrypted with the given password. Casts the object to JSON, then byte[]
        /// </summary>
        public static bool SetSecurePlayerDataJson(string playerId, string key, object value, string password, double? expiration = null)
        {
            return SetSecurePlayerData(playerId, key, value.ToJsonByteArray(), password, expiration);
        }

        /// <summary>
        /// Saves a key/value pair to a given player's AccountID with a password.
        /// </summary>
        /// <param name="playerId">the account ID for the player.</param>
        /// <param name="key">The key to save to the database. Keys are unique, and you cannot have multiples of the same key.</param>
        /// <param name="value">The value to save with the key.</param>
        /// <param name="password"></param>
        /// <param name="expiration">If not null, expire this data in this many seconds from now.</param>
        /// <returns>true if data was saved, false if data was not.</returns>
        public static bool SetSecurePlayerData(string playerId, string key, byte[] value, string password, double? expiration = null)
        {
            var encryptedValue = EncryptValue(value, password, out byte[] IVs);

            using var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var row = db.PlayerData.FirstOrDefault(p => p.accountId == playerId && p.DataKey == key);
            if (row == null)
            {
                row = new DbTables.PlayerData();
                row.DataKey = key;
                row.accountId = playerId;
                db.PlayerData.Add(row);
            }
            else
                db.Entry(row).State = EntityState.Modified;

            if (expiration.HasValue)
                row.Expiration = DateTime.UtcNow.AddSeconds(expiration.Value);
            else
                row.Expiration = null;
            row.IvData = IVs;
            row.DataValue = encryptedValue;

            if (enableCaching && memoryCache != null)
            {
                memoryCache.Set(playerId + "-" + key, row, new DateTimeOffset(expiration == null ? DateTime.UtcNow.AddMinutes(15) : DateTime.UtcNow.AddSeconds(expiration.Value)));
            }

            return db.SaveChanges() == 1;
        }

        /// <summary>
        /// Get the value from a key/value pair saved on a Place. Expired entries will be ignored.
        /// </summary>
        /// <param name="elementId">the Guid exposed to clients to identify the map element.</param>
        /// <param name="key">The key to load from the database. Keys are unique, and you cannot have multiples of the same key.</param>
        /// <returns>The value saved to the key, or an empty string if no key/value pair was found.</returns>
        public static byte[] GetSecurePlaceData(Guid elementId, string key, string password)
        {
            if (enableCaching && memoryCache != null)
            {
                if (memoryCache.TryGetValue(elementId.ToString() + "-" + key, out PlaceData results))
                    return DecryptValue(results.IvData, results.DataValue, password);
            }

            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var row = db.PlaceData.Include(p => p.Place).FirstOrDefault(p => p.Place.PrivacyId == elementId && p.DataKey == key);
            if (row == null || row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.UtcNow)
                return Array.Empty<byte>();
            return DecryptValue(row.IvData, row.DataValue, password);
        }

        /// <summary>
        /// Get the value from a key/value pair saved on a Place and casts it to the requested type. Expired entries will be ignored.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="elementId"></param>
        /// <param name="key"></param>
        /// <param name="password"></param>
        /// <returns></returns>
        public static T GetSecurePlaceData<T>(Guid elementId, string key, string password)
        {
            return GetSecurePlaceData(elementId, key, password).FromJsonBytesTo<T>();
        }

        /// <summary>
        /// Saves a key/value pair to a given Place with the given password.
        /// </summary>
        /// <param name="elementId"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="password"></param>
        /// <param name="expiration"></param>
        /// <returns></returns>
        public static bool SetSecurePlaceData(Guid elementId, string key, string value, string password, double? expiration = null)
        {
            return SetSecurePlaceData(elementId, key, value.ToByteArrayUTF8(), password, expiration);
        }

        /// <summary>
        /// Saves a key/value pair to a given Place with the given password, and casts it to JSON then byte[].
        /// </summary>
        /// <param name="elementId"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="password"></param>
        /// <param name="expiration"></param>
        /// <returns></returns>
        public static bool SetSecurePlaceDataJson(Guid elementId, string key, object value, string password, double? expiration = null)
        {
            return SetSecurePlaceData(elementId, key, value.ToJsonByteArray(), password, expiration);
        }

        /// <summary>
        /// Saves a key/value pair to a given Place with the given password
        /// </summary>
        /// <param name="elementId">the Guid exposed to clients to identify the map element.</param>
        /// <param name="key">The key to save to the database for the map element.</param>
        /// <param name="value">The value to save with the key.</param>
        /// <param name="password">The password to encrypt the value with.</param>
        /// <param name="expiration">If not null, expire this data in this many seconds from now.</param>
        /// <returns>true if data was saved, false if data was not.</returns>
        public static bool SetSecurePlaceData(Guid elementId, string key, byte[] value, string password, double? expiration = null)
        {
            byte[] encryptedValue = EncryptValue(value, password, out byte[] IVs);
            using var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;

            var row = db.PlaceData.Include(p => p.Place).FirstOrDefault(p => p.Place.PrivacyId == elementId && p.DataKey == key);
            if (row == null)
            {
                var sourceItem = db.Places.First(p => p.PrivacyId == elementId);
                row = new DbTables.PlaceData();
                row.DataKey = key;
                row.Place = sourceItem;
                db.PlaceData.Add(row);
            }
            else
                db.Entry(row).State = EntityState.Modified;

            if (expiration.HasValue)
                row.Expiration = DateTime.UtcNow.AddSeconds(expiration.Value);
            else
                row.Expiration = null;
            row.IvData = IVs;
            row.DataValue = encryptedValue;

            if (enableCaching && memoryCache != null)
            {
                memoryCache.Set(elementId.ToString() + "-" + key, row, new DateTimeOffset(expiration == null ? DateTime.UtcNow.AddMinutes(15) : DateTime.UtcNow.AddSeconds(expiration.Value)));
            }

            return db.SaveChanges() == 1;
        }

        //NOTE: this returns the entry for 'key' for all players. Not all entries for a player.
        /// <summary>
        /// Returns the entries for all accounts with the given key. Use GetAllPlayerData to get all keys for 1 account.
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public static List<PlayerData> GetAllPlayerDataByKey(string key)
        {
            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var results = db.PlayerData.Where(k => k.DataKey == key && k.IvData == null).ToList();
            return results;
        }

        /// <summary>
        /// Aquires a lock, loads the given key, increments it by the given value, and then saves it back to the database. Ideal for tracking team scores or similar frequently-changed values.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        public static void IncrementGlobalData(string key, double value)
        {
            SimpleLockable.PerformWithLock("global" + key, () =>
            {
                byte[] sourcevalue = Array.Empty<byte>();
                using var db = new PraxisContext();
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                var row = db.GlobalData.FirstOrDefault(s => s.DataKey == key);
                if (row == null)
                {
                    row = new DbTables.GlobalData();
                    row.DataKey = key;
                    db.GlobalData.Add(row);
                }
                else
                {
                    db.Entry(row).State = EntityState.Modified;
                    sourcevalue = row.DataValue;
                }
                _ = Double.TryParse(sourcevalue.ToUTF8String(), out double val);
                val += value;
                row.DataValue = val.ToString().ToByteArrayUTF8();
                db.SaveChanges();
            });
        }

        /// <summary>
        /// Aquires a lock, loads the given key for the account, increments it by the given value, and then saves it back to the database.
        /// </summary>
        /// <param name="playerId"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="expiration"></param>
        public static void IncrementPlayerData(string playerId, string key, double value, double? expiration = null)
        {
            SimpleLockable.PerformWithLock(playerId + key, () =>
            {
                byte[] sourcevalue = Array.Empty<byte>();
                using var db = new PraxisContext();
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                var row = db.PlayerData.FirstOrDefault(s => s.accountId == playerId && s.DataKey == key);
                if (row == null)
                {
                    row = new DbTables.PlayerData();
                    row.accountId = playerId;
                    row.DataKey = key;
                    db.PlayerData.Add(row);
                }
                else if (row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.UtcNow)
                {
                    db.Entry(row).State = EntityState.Modified;
                }
                else
                {
                    db.Entry(row).State = EntityState.Modified;
                    sourcevalue = row.DataValue;
                }

                if (expiration.HasValue)
                    row.Expiration = DateTime.UtcNow.AddSeconds(expiration.Value);
                else
                    row.Expiration = null;

                _ = Double.TryParse(sourcevalue.ToUTF8String(), out double val);
                val += value;
                row.DataValue = val.ToString().ToByteArrayUTF8();
                db.SaveChanges();
            });
        }

        /// <summary>
        /// Aquires a lock, loads the given key for the Place, increments it by the given value, and then saves it back to the database. Ideal for tracking scores or states that change frequently.
        /// </summary>
        /// <param name="placeId"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="expiration"></param>
        public static void IncrementPlaceData(Guid placeId, string key, double value, double? expiration = null)
        {
            SimpleLockable.PerformWithLock(placeId + key, () =>
            {
                byte[] sourcevalue = Array.Empty<byte>();
                using var db = new PraxisContext();
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                var row = db.PlaceData.Include(p => p.Place).FirstOrDefault(p => p.Place.PrivacyId == placeId && p.DataKey == key);
                if (row == null)
                {
                    var sourceItem = db.Places.Where(p => p.PrivacyId == placeId).Select(p => p.Id).FirstOrDefault();
                    row = new DbTables.PlaceData();
                    row.PlaceId = sourceItem;
                    row.DataKey = key;
                    db.PlaceData.Add(row);
                }
                else if (row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.UtcNow)
                {
                    db.Entry(row).State = EntityState.Modified;
                }
                else
                {
                    sourcevalue = row.DataValue;
                    db.Entry(row).State = EntityState.Modified;
                }

                if (expiration.HasValue)
                    row.Expiration = DateTime.UtcNow.AddSeconds(expiration.Value);
                else
                    row.Expiration = null;

                _ = Double.TryParse(sourcevalue.ToUTF8String(), out double val);
                val += value;
                row.DataValue = val.ToString().ToByteArrayUTF8();
                db.SaveChanges();
            });
        }

        /// <summary>
        /// Aquires a lock, loads the given key for the Area, increments it by the given value, and then saves it back to the database. Ideal for tracking team scores or similar frequently-changed values.
        /// </summary>
        /// <param name="plusCode"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="expiration"></param>
        public static void IncrementAreaData(string plusCode, string key, double value, double? expiration = null)
        {
            SimpleLockable.PerformWithLock(plusCode + key, () =>
            {
                byte[] sourcevalue = Array.Empty<byte>();
                using var db = new PraxisContext();
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                var row = db.AreaData.FirstOrDefault(s => s.PlusCode == plusCode && s.DataKey == key);
                if (row == null)
                {
                    row = new DbTables.AreaData();
                    row.PlusCode = plusCode;
                    row.AreaCovered = plusCode.ToPolygon();
                    row.DataKey = key;
                    db.AreaData.Add(row);
                }
                else if (row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.UtcNow)
                {
                    db.Entry(row).State = EntityState.Modified;
                }
                else
                {
                    db.Entry(row).State = EntityState.Modified;
                    sourcevalue = row.DataValue;
                }

                if (expiration.HasValue)
                    row.Expiration = DateTime.UtcNow.AddSeconds(expiration.Value);
                else
                    row.Expiration = null;

                _ = Double.TryParse(sourcevalue.ToUTF8String(), out double val);
                val += value;
                row.DataValue = val.ToString().ToByteArrayUTF8();
                db.SaveChanges();

                if (enableCaching && memoryCache != null)
                {
                    memoryCache.Set(plusCode + "-" + key, value, new DateTimeOffset(expiration == null ? DateTime.UtcNow.AddMinutes(15) : DateTime.UtcNow.AddSeconds(expiration.Value)));
                }
            });
        }

        /// <summary>
        /// Acquires a lock, loads the given key for the player, increments it by the given value, and then encrypts and saves it back to the database.
        /// </summary>
        /// <param name="playerId"></param>
        /// <param name="password"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="expiration"></param>
        public static void IncrementSecurePlayerData(string playerId, string password, string key, double value, double? expiration = null)
        {
            SimpleLockable.PerformWithLock(playerId + "secure" + key, () =>
            {
                byte[] sourceValue = Array.Empty<byte>();
                using var db = new PraxisContext();
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                var row = db.PlayerData.FirstOrDefault(p => p.accountId == playerId && p.DataKey == key);
                if (row == null)
                {
                    row = new DbTables.PlayerData();
                    row.accountId = playerId;
                    row.DataKey = key;
                    db.PlayerData.Add(row);

                }
                else if (row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.UtcNow)
                {
                    db.Entry(row).State = EntityState.Modified;
                }
                else
                {
                    sourceValue = DecryptValue(row.IvData, row.DataValue, password);
                    db.Entry(row).State = EntityState.Modified;
                }

                if (expiration.HasValue)
                    row.Expiration = DateTime.UtcNow.AddSeconds(expiration.Value);
                else
                    row.Expiration = null;

                _ = Double.TryParse(sourceValue.ToUTF8String(), out double val);
                val += value;
                var encryptedValue = EncryptValue(val.ToString().ToByteArrayUTF8(), password, out byte[] IVs);
                row.DataValue = encryptedValue;
                row.IvData = IVs;
                db.SaveChanges();

                if (enableCaching && memoryCache != null)
                {
                    memoryCache.Set(playerId + "-" + key, encryptedValue, new DateTimeOffset(expiration == null ? DateTime.UtcNow.AddMinutes(15) : DateTime.UtcNow.AddSeconds(expiration.Value)));
                }
            });
        }

        /// <summary>
        /// Aquires a lock, loads the given key for a Place, increments it by the given value, and then encrypts and saves it back to the database. Ideal for tracking team scores or similar frequently-changed values.
        /// </summary>
        /// <param name="placeId"></param>
        /// <param name="password"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="expiration"></param>
        public static void IncrementSecurePlaceData(Guid placeId, string password, string key, double value, double? expiration = null)
        {
            SimpleLockable.PerformWithLock(placeId + "secure" + key, () =>
            {
                byte[] sourceValue = Array.Empty<byte>();
                using var db = new PraxisContext();
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                var row = db.PlaceData.Include(p => p.Place).FirstOrDefault(p => p.Place.PrivacyId == placeId && p.DataKey == key);
                if (row == null)
                {
                    var sourceItem = db.Places.Where(p => p.PrivacyId == placeId).Select(p => p.Id).FirstOrDefault();
                    row = new DbTables.PlaceData();
                    row.PlaceId = sourceItem;
                    row.DataKey = key;
                    db.PlaceData.Add(row);

                }
                else if (row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.UtcNow)
                {
                    db.Entry(row).State = EntityState.Modified;
                }
                else
                {
                    sourceValue = DecryptValue(row.IvData, row.DataValue, password);
                    db.Entry(row).State = EntityState.Modified;
                }

                if (expiration.HasValue)
                    row.Expiration = DateTime.UtcNow.AddSeconds(expiration.Value);
                else
                    row.Expiration = null;

                _ = Double.TryParse(sourceValue.ToUTF8String(), out double val);
                val += value;
                var encryptedValue = EncryptValue(val.ToString().ToByteArrayUTF8(), password, out byte[] IVs);
                row.DataValue = encryptedValue;
                row.IvData = IVs;
                db.SaveChanges();

                if (enableCaching && memoryCache != null)
                {
                    memoryCache.Set(placeId.ToString() + "-" + key, encryptedValue, new DateTimeOffset(expiration == null ? DateTime.UtcNow.AddMinutes(15) : DateTime.UtcNow.AddSeconds(expiration.Value)));
                }
            });
        }

        /// <summary>
        /// Aquires a lock, loads the given key for the Area, increments it by the given value, and then encrypts and saves it back to the database. Ideal for tracking team scores or similar frequently-changed values.
        /// </summary>
        /// <param name="plusCode"></param>
        /// <param name="password"></param>
        /// <param name="key"></param>
        /// <param name="value"></param>
        /// <param name="expiration"></param>
        public static void IncrementSecureAreaData(string plusCode, string password, string key, double value, double? expiration = null)
        {
            SimpleLockable.PerformWithLock(plusCode + "secure" + key, () =>
            {
                byte[] sourceValue = Array.Empty<byte>();
                using var db = new PraxisContext();
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                var row = db.AreaData.FirstOrDefault(p => p.PlusCode == plusCode && p.DataKey == key);
                if (row == null)
                {
                    row = new DbTables.AreaData();
                    row.PlusCode = plusCode;
                    row.DataKey = key;
                    db.AreaData.Add(row);
                }
                else if (row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.UtcNow)
                {
                    db.Entry(row).State = EntityState.Modified;
                }
                else
                {
                    sourceValue = DecryptValue(row.IvData, row.DataValue, password);
                    db.Entry(row).State = EntityState.Modified;
                }

                if (expiration.HasValue)
                    row.Expiration = DateTime.UtcNow.AddSeconds(expiration.Value);
                else
                    row.Expiration = null;

                _ = Double.TryParse(sourceValue.ToUTF8String(), out double val);
                val += value;
                var encryptedValue = EncryptValue(val.ToString().ToByteArrayUTF8(), password, out byte[] IVs);
                row.DataValue = encryptedValue;
                row.IvData = IVs;
                db.SaveChanges();

                if (enableCaching && memoryCache != null)
                {
                    memoryCache.Set(plusCode + "-" + key, encryptedValue, new DateTimeOffset(expiration == null ? DateTime.UtcNow.AddMinutes(15) : DateTime.UtcNow.AddSeconds(expiration.Value)));
                }

            });
        }

        /// <summary>
        /// Given a value in byte[] form and a password, encrypts the value with the password. Provides the encrypted value as a return value, and the IVs needed to decrypt it as an out parameter
        /// </summary>
        /// <param name="value"></param>
        /// <param name="password"></param>
        /// <param name="IVs"></param>
        /// <returns></returns>
        public static byte[] EncryptValue(byte[] value, string password, out byte[] IVs)
        {
            byte[] passwordBytes = SHA256.HashData(password.ToByteArrayUTF8());
            baseSec.GenerateIV();
            IVs = baseSec.IV;
            var crypter = baseSec.CreateEncryptor(passwordBytes, IVs);

            using var ms = new MemoryStream();
            using (CryptoStream cs = new CryptoStream(ms, crypter, CryptoStreamMode.Write))
                cs.Write(value, 0, value.Length);

            return ms.ToArray();
        }

        /// <summary>
        /// Decrypts a value in byte[] form, given the password and IVs used to encrypt it.
        /// </summary>
        /// <param name="IVs"></param>
        /// <param name="value"></param>
        /// <param name="password"></param>
        /// <returns></returns>
        public static byte[] DecryptValue(byte[] IVs, byte[] value, string password)
        {
            byte[] passwordBytes = SHA256.HashData(password.ToByteArrayUTF8());

            var crypter = baseSec.CreateDecryptor(passwordBytes, IVs);

            using var ms = new MemoryStream();
            using (CryptoStream cs = new CryptoStream(ms, crypter, CryptoStreamMode.Write))
                cs.Write(value);
            return ms.ToArray();
        }

        /// <summary>
        /// Reads the body of a HttpRequest to byte[] form, using it's PipeReader.
        /// </summary>
        /// <param name="br"></param>
        /// <param name="contentLength"></param>
        /// <returns></returns>
        public static byte[] ReadBody(PipeReader br, long? contentLength)
        {
            return ReadBody(br, (int)contentLength);
        }

        /// <summary>
        /// Reads the body of a HttpRequest to byte[] form, using it's PipeReader.
        /// </summary>
        /// <param name="br"></param>
        /// <param name="contentLength"></param>
        /// <returns></returns>
        public static byte[] ReadBody(PipeReader br, int contentLength)
        {
            var rr = br.ReadAtLeastAsync(contentLength);
            var wait = rr.GetAwaiter();

            while (!wait.IsCompleted)
                System.Threading.Thread.Sleep(1);
            var endData = rr.Result.Buffer.ToArray();
            br.AdvanceTo(rr.Result.Buffer.Start); // this is required to silence an error in Kestrel on Linux.
            return endData;
        }

        /// <summary>
        /// Creates a password for a PraxisMapper account, using BCrypt and the rounds option provided. These are SUPPOSED to be very slow to generate, to discourage brute force attacks.
        /// Saves results directly to the AuthenticationData table. This password is used to authenticate a user on login. The password used to store data securely is pulled with GetInternalPassword()
        /// </summary>
        /// <param name="accountId"></param>
        /// <param name="password"></param>
        /// <param name="rounds"></param>
        /// <returns></returns>
        public static bool EncryptPassword(string accountId, string password, int rounds)
        {
            var hashedPwd = BCrypt.Net.BCrypt.EnhancedHashPassword(password, rounds);

            using var db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var entry = db.AuthenticationData.Where(a => a.accountId == accountId).FirstOrDefault();
            if (entry == null)
            {
                entry = new DbTables.AuthenticationData();
                db.AuthenticationData.Add(entry);
                entry.accountId = accountId;
                var bytes = EncryptValue(Guid.NewGuid().ToByteArray(), password, out var IVs);
                entry.dataPassword = Convert.ToBase64String(bytes);
                entry.dataIV = IVs;
            }
            else
            {
                db.Entry(entry).State = EntityState.Modified;
                var bytes = Convert.FromBase64String(entry.dataPassword);
                var intPwd = new Guid(DecryptValue(entry.dataIV, bytes, password));
                bytes = EncryptValue(intPwd.ToByteArray(), password, out var IVs);
                entry.dataPassword = Convert.ToBase64String(bytes);
                entry.dataIV = IVs;
            }
            entry.loginPassword = hashedPwd;
            db.SaveChanges();

            return true;
        }

        /// <summary>
        /// Checks the login password for a given user account. Returns true if the given password is correct, false if incorrect OR if the user is banned. This is intentionally a slow method.
        /// </summary>
        /// <param name="accountId"></param>
        /// <param name="password"></param>
        /// <returns></returns>
        public static bool CheckPassword(string accountId, string password, bool ignoreBan = false)
        {
            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var entry = db.AuthenticationData.Where(a => a.accountId == accountId).FirstOrDefault();
            if (entry == null)
                return false;
            if (entry.bannedUntil.HasValue && entry.bannedUntil.Value > DateTime.UtcNow && ignoreBan == false)
                return false;

            bool results = BCrypt.Net.BCrypt.EnhancedVerify(password, entry.loginPassword);
            return results;
        }

        /// <summary>
        /// Retrieves the internal password for an account. This password is used to store data in the database securely for a player, not the password they logged in with.
        /// This separation is done to keep user passwords out of memory as a security precaution.
        /// </summary>
        /// <param name="accountId"></param>
        /// <param name="password"></param>
        /// <returns></returns>
        public static string GetInternalPassword(string accountId, string password)
        {
            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var entry = db.AuthenticationData.Where(a => a.accountId == accountId).FirstOrDefault();
            var bytes = Convert.FromBase64String(entry.dataPassword);
            var intPwd = new Guid(DecryptValue(entry.dataIV, bytes, password)).ToString();

            return intPwd;
        }

        public static T? DeserializeAnonymousType<T>(string json, T anonymousTypeObject)
        {
            return JsonSerializer.Deserialize<T>(json);
        }
    }
}
