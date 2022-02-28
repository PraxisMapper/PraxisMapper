using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using PraxisCore.Support;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace PraxisCore
{
    /// <summary>
    /// Classes that handle reading and writing key-value pairs for players, places, pluscodes, or the global set.
    /// </summary>
    public static class GenericData
    {
        /// <summary>
        /// Saves a key/value pair to a given PlusCode. Will reject a pair containing a player's deviceId in the database.
        /// </summary>
        /// <param name="plusCode">A valid PlusCode, excluding the + symbol.</param>
        /// <param name="key">The key to save to the database. Keys are unique, and you cannot have multiples of the same key.</param>
        /// <param name="value">The value to save with the key.</param>
        /// <param name="expiration">If not null, expire this data in this many seconds from now.</param>
        /// <returns>true if data was saved, false if data was not.</returns>
        /// 
        public static Aes baseSec = Aes.Create();

        public static bool SetPlusCodeData(string plusCode, string key, string value, double? expiration = null)
        {
            var db = new PraxisContext();
            if (db.PlayerData.Any(p => p.DeviceID == key || p.DeviceID == value))
                return false;

            var row = db.CustomDataPlusCodes.FirstOrDefault(p => p.PlusCode == plusCode && p.DataKey == key);
            if (row == null)
            {
                row = new DbTables.CustomDataPlusCode();
                row.DataKey = key;
                row.PlusCode = plusCode;
                row.GeoAreaIndex = Converters.GeoAreaToPolygon(OpenLocationCode.DecodeValid(plusCode.ToUpper()));
                db.CustomDataPlusCodes.Add(row);
            }
            if (expiration.HasValue)
                row.Expiration = DateTime.Now.AddSeconds(expiration.Value);
            else
                row.Expiration = null;
            row.DataValue = value;
            return db.SaveChanges() == 1;
        }

        /// <summary>
        /// Get the value from a key/value pair saved on a PlusCode. Expired values will not be sent over.
        /// </summary>
        /// <param name="plusCode">A valid PlusCode, excluding the + symbol.</param>
        /// <param name="key">The key to load from the database on the PlusCode</param>
        /// <returns>The value saved to the key, or an empty string if no key/value pair was found.</returns>
        public static string GetPlusCodeData(string plusCode, string key)
        {
            var db = new PraxisContext();
            var row = db.CustomDataPlusCodes.FirstOrDefault(p => p.PlusCode == plusCode && p.DataKey == key);
            if (row == null || row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.Now)
                return "";
            return row.DataValue;
        }

        /// <summary>
        /// Saves a key/value pair to a given map element. Will reject a pair containing a player's deviceId in the database.
        /// </summary>
        /// <param name="elementId">the Guid exposed to clients to identify the map element.</param>
        /// <param name="key">The key to save to the database for the map element.</param>
        /// <param name="value">The value to save with the key.</param>
        /// <param name="expiration">If not null, expire this data in this many seconds from now.</param>
        /// <returns>true if data was saved, false if data was not.</returns>
        public static bool SetStoredElementData(Guid elementId, string key, string value, double? expiration = null)
        {
            var db = new PraxisContext();
            if (db.PlayerData.Any(p => p.DeviceID == key || p.DeviceID == value))
                return false;

            var row = db.CustomDataOsmElements.Include(p => p.StoredOsmElement).FirstOrDefault(p => p.StoredOsmElement.PrivacyId == elementId && p.DataKey == key);
            if (row == null)
            {
                var sourceItem = db.StoredOsmElements.First(p => p.PrivacyId == elementId);
                row = new DbTables.CustomDataOsmElement();
                row.DataKey = key;
                row.StoredOsmElement = sourceItem;
                db.CustomDataOsmElements.Add(row);
            }
            if (expiration.HasValue)
                row.Expiration = DateTime.Now.AddSeconds(expiration.Value);
            else
                row.Expiration = null;
            row.DataValue = value;
            return db.SaveChanges() == 1;
        }

        /// <summary>
        /// Get the value from a key/value pair saved on a map element. Expired entries will be ignored.
        /// </summary>
        /// <param name="elementId">the Guid exposed to clients to identify the map element.</param>
        /// <param name="key">The key to load from the database. Keys are unique, and you cannot have multiples of the same key.</param>
        /// <returns>The value saved to the key, or an empty string if no key/value pair was found.</returns>
        public static string GetElementData(Guid elementId, string key)
        {
            var db = new PraxisContext();
            var row = db.CustomDataOsmElements.Include(p => p.StoredOsmElement).FirstOrDefault(p => p.StoredOsmElement.PrivacyId == elementId && p.DataKey == key);
            if (row == null || row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.Now)
                return "";
            return row.DataValue;
        }

        /// <summary>
        /// Get the value from a key/value pair saved on a player's deviceId. Expired entries will be ignored.
        /// </summary>
        /// <param name="playerId">the player-specific ID used. Expected to be a unique DeviceID to identify a phone, per that device's rules.</param>
        /// <param name="key">The key to load data from for the playerId.</param>
        /// <returns>The value saved to the key, or an empty string if no key/value pair was found.</returns>
        public static string GetPlayerData(string playerId, string key)
        {
            var db = new PraxisContext();
            var row = db.PlayerData.FirstOrDefault(p => p.DeviceID == playerId && p.DataKey == key);
            if (row == null || row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.Now)
                return "";
            else
                row.Expiration = null;
            return row.DataValue;
        }

        /// <summary>
        /// Saves a key/value pair to a given player's DeviceID. Will reject a pair containing a PlusCode or map element Id.
        /// </summary>
        /// <param name="playerId"></param>
        /// <param name="key">The key to save to the database. Keys are unique, and you cannot have multiples of the same key.</param>
        /// <param name="value">The value to save with the key.</param>
        /// <param name="expiration">If not null, expire this data in this many seconds from now.</param>
        /// <returns>true if data was saved, false if data was not.</returns>
        public static bool SetPlayerData(string playerId, string key, string value, double? expiration = null)
        {
            if (DataCheck.IsPlusCode(key) || DataCheck.IsPlusCode(value))
                return false; //Reject attaching a player to a pluscode.

            var db = new PraxisContext();
            Guid tempCheck = new Guid();
            if ((Guid.TryParse(key, out tempCheck) && db.StoredOsmElements.Any(osm => osm.PrivacyId == tempCheck))
                || (Guid.TryParse(value, out tempCheck) && db.StoredOsmElements.Any(osm => osm.PrivacyId == tempCheck)))
                return false; //reject attaching a player to an area

            var row = db.PlayerData.FirstOrDefault(p => p.DeviceID == playerId && p.DataKey == key);
            if (row == null)
            {
                row = new DbTables.PlayerData();
                row.DataKey = key;
                row.DeviceID = playerId;
                db.PlayerData.Add(row);
            }
            if (expiration.HasValue)
                row.Expiration = DateTime.Now.AddSeconds(expiration.Value);
            else
                row.Expiration = null;
            row.DataValue = value;
            return db.SaveChanges() == 1;
        }

        /// <summary>
        /// Load all of the key/value pairs in a PlusCode, including pairs saved to a longer PlusCode. Expired entries will be ignored.
        /// (EX: calling this with an 8 character PlusCode will load all contained 10 character PlusCodes key/value pairs)
        /// </summary>
        /// <param name="plusCode">A valid PlusCode, excluding the + symbol.</param>
        /// <returns>a List of results with the PlusCode, keys, and values</returns>
        public static List<CustomDataResult> GetAllDataInPlusCode(string plusCode) //TODO: add optional key filter
        {
            var db = new PraxisContext();
            var plusCodeArea = OpenLocationCode.DecodeValid(plusCode);
            var plusCodePoly = Converters.GeoAreaToPolygon(plusCodeArea);
            var plusCodeData = db.CustomDataPlusCodes.Where(d => plusCodePoly.Intersects(d.GeoAreaIndex))
                .ToList() //Required to run the next Where on the C# side
                .Where(row => row.Expiration.GetValueOrDefault(DateTime.MaxValue) > DateTime.Now)
                .Select(d => new CustomDataResult(d.PlusCode, d.DataKey, d.DataValue.Length > 512 ? "truncated" : d.DataValue))
                .ToList();

            return plusCodeData;
        }

        /// <summary>
        /// Load all of the key/value pairs in a GeoArea attached to a PlusCode. Expired entries will be ignored.
        /// </summary>
        /// <param name="area">the GeoArea to pull data for.</param>
        /// <returns>a List of results with the PlusCode, keys, and values</returns>
        public static List<CustomDataResult> GetAllPlusCodeDataInArea(GeoArea area, string key = "")
        {
            var db = new PraxisContext();
            var poly = Converters.GeoAreaToPolygon(area);
            var plusCodeData = db.CustomDataPlusCodes.Where(d => poly.Intersects(d.GeoAreaIndex) && (key == "" || d.DataKey == key))
                .ToList() //Required to run the next Where on the C# side
                .Where(row => row.Expiration.GetValueOrDefault(DateTime.MaxValue) > DateTime.Now)
                .Select(d => new CustomDataResult(d.PlusCode, d.DataKey, d.DataValue.Length > 512? "truncated" : d.DataValue))
                .ToList();

            return plusCodeData;
        }

        /// <summary>
        /// Load all of the key/value pairs in a GeoArea attached to a map element. Expired entries will be ignored.
        /// </summary>
        /// <param name="area">the GeoArea to pull data for.</param>
        /// <returns>a List of results with the map element ID, keys, and values</returns>
        public static List<CustomDataAreaResult> GetAllDataInArea(GeoArea area, string key = "")
        {
            var db = new PraxisContext();
            var poly = Converters.GeoAreaToPolygon(area);
            var data = db.CustomDataOsmElements.Include(d => d.StoredOsmElement).Where(d => poly.Intersects(d.StoredOsmElement.ElementGeometry) && (key == "" || d.DataKey == key))
                .ToList() //Required to run the next Where on the C# side
                .Where(row => row.Expiration.GetValueOrDefault(DateTime.MaxValue) > DateTime.Now)
                .Select(d => new CustomDataAreaResult(d.StoredOsmElement.PrivacyId, d.DataKey, d.DataValue.Length > 512 ? "truncated" : d.DataValue))
                .ToList();

            return data;
        }

        /// <summary>
        /// Load all of the key/value pairs attached to a map element. Expired entries will be ignored.
        /// </summary>
        /// <param name="elementId">the Guid exposed to clients to identify the map element.</param>
        /// <returns>a List of results with the map element ID, keys, and values</returns>
        public static List<CustomDataAreaResult> GetAllDataInPlace(Guid elementId, string key = "")
        {
            var db = new PraxisContext();
            var place = db.StoredOsmElements.First(s => s.PrivacyId == elementId);
            var data = db.CustomDataOsmElements.Where(d => d.StoredOsmElement.ElementGeometry.Intersects(d.StoredOsmElement.ElementGeometry) && (key == "" || d.DataKey == key))
                .ToList() //Required to run the next Where on the C# side
                .Where(row => row.Expiration.GetValueOrDefault(DateTime.MaxValue) > DateTime.Now)
                .Select(d => new CustomDataAreaResult(place.PrivacyId, d.DataKey, d.DataValue.Length > 512 ? "truncated" : d.DataValue))
                .ToList();

            return data;
        }

        /// <summary>
        /// Returns all data attached to a player's device ID
        /// </summary>
        /// <param name="deviceID">the device associated with a player</param>
        /// <returns>List of reuslts with deviceId, keys, and values</returns>
        public static List<CustomDataPlayerResult> GetAllPlayerData(string deviceID)
        {
            var db = new PraxisContext();
            var data = db.PlayerData.Where(p => p.DeviceID == deviceID)
                .ToList()
                .Where(row => row.Expiration.GetValueOrDefault(DateTime.MaxValue) > DateTime.Now)
                .Select(d => new CustomDataPlayerResult(d.DeviceID, d.DataKey, d.DataValue.Length > 512 ? "truncated" : d.DataValue))
                .ToList();

            return data;
        }

        /// <summary>
        /// Loads a key/value pair from the database that isn't attached to anything specific. Global entries do not expire.
        /// </summary>
        /// <param name="key">The key to load data from.</param>
        /// <returns>The value saved to the key, or an empty string if no key/value pair was found.</returns>
        public static string GetGlobalData(string key)
        {
            var db = new PraxisContext();
            var row = db.GlobalDataEntries.FirstOrDefault(s => s.DataKey == key);
            if (row == null)
                return "";

            return row.DataValue;
        }

        /// <summary>
        /// Saves a key/value pair to the database that isn't attached to anything specific. Wil reject a pair that contains a player's device ID, PlusCode, or a map element ID. Global entries cannot be set to expire.
        /// </summary>
        /// <param name="key">The key to save to the database. Keys are unique, and you cannot have multiples of the same key.</param>
        /// <param name="value">The value to save with the key.</param>
        /// <returns>true if data was saved, false if data was not.</returns>
        public static bool SetGlobalData(string key, string value)
        {
            bool trackingPlayer = false;
            bool trackingLocation = false;

            var db = new PraxisContext();
            if (db.PlayerData.Any(p => p.DeviceID == key || p.DeviceID == value))
                trackingPlayer = true;

            if (DataCheck.IsPlusCode(key) || DataCheck.IsPlusCode(value))
                trackingLocation = true;

            Guid tempCheck = new Guid();
            if ((Guid.TryParse(key, out tempCheck) && db.StoredOsmElements.Any(osm => osm.PrivacyId == tempCheck))
                || (Guid.TryParse(value, out tempCheck) && db.StoredOsmElements.Any(osm => osm.PrivacyId == tempCheck)))
                trackingLocation = true;

            if (trackingLocation && trackingPlayer) //Do not allow players and locations to be attached on the global level as a workaround to being blocked on the individual levels.
                return false;

            var row = db.GlobalDataEntries.FirstOrDefault(p => p.DataKey == key);
            if (row == null)
            {
                row = new DbTables.GlobalDataEntries();
                row.DataKey = key;
                db.GlobalDataEntries.Add(row);
            }
            row.DataValue = value;
            return db.SaveChanges() == 1;
        }

        public static bool SetSecurePlusCodeData(string plusCode, string key, string value, string password, double? expiration = null)
        {
            var db = new PraxisContext();
            string encryptedValue = EncryptValue(value, password);

            var row = db.CustomDataPlusCodes.FirstOrDefault(p => p.PlusCode == plusCode && p.DataKey == key);
            if (row == null)
            {
                row = new DbTables.CustomDataPlusCode();
                row.DataKey = key;
                row.PlusCode = plusCode;
                row.GeoAreaIndex = Converters.GeoAreaToPolygon(OpenLocationCode.DecodeValid(plusCode.ToUpper()));
                db.CustomDataPlusCodes.Add(row);
            }
            if (expiration.HasValue)
                row.Expiration = DateTime.Now.AddSeconds(expiration.Value);
            else
                row.Expiration = null;

            row.DataValue = encryptedValue;
            return db.SaveChanges() == 1;
        }

        public static bool SetSecurePlusCodeData(string plusCode, string key, byte[] value, string password, double? expiration = null)
        {
            var db = new PraxisContext();
            string encryptedValue = EncryptValue(value, password);

            var row = db.CustomDataPlusCodes.FirstOrDefault(p => p.PlusCode == plusCode && p.DataKey == key);
            if (row == null)
            {
                row = new DbTables.CustomDataPlusCode();
                row.DataKey = key;
                row.PlusCode = plusCode;
                row.GeoAreaIndex = Converters.GeoAreaToPolygon(OpenLocationCode.DecodeValid(plusCode.ToUpper()));
                db.CustomDataPlusCodes.Add(row);
            }
            if (expiration.HasValue)
                row.Expiration = DateTime.Now.AddSeconds(expiration.Value);
            else
                row.Expiration = null;

            row.DataValue = encryptedValue;
            return db.SaveChanges() == 1;
        }

        /// <summary>
        /// Get the value from a key/value pair saved on a PlusCode with a password. Expired values will not be sent over.
        /// </summary>
        /// <param name="plusCode">A valid PlusCode, excluding the + symbol.</param>
        /// <param name="key">The key to load from the database on the PlusCode</param>
        /// <param name="password">The password used to encrypt the value originally.</param>
        /// <returns>The value saved to the key, or an empty string if no key/value pair was found or the password is incorrect.</returns>
        public static string GetSecurePlusCodeData(string plusCode, string key, string password)
        {
            var db = new PraxisContext();
            var row = db.CustomDataPlusCodes.FirstOrDefault(p => p.PlusCode == plusCode && p.DataKey == key);
            if (row == null || row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.Now)
                return "";

            return DecryptValue(row.DataValue, password);
        }

        //public static byte[] GetSecurePlusCodeDataBytes(string plusCode, string key, string password)
        //{
        //    var db = new PraxisContext();
        //    var row = db.CustomDataPlusCodes.FirstOrDefault(p => p.PlusCode == plusCode && p.dataKey == key);
        //    if (row == null || row.expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.Now)
        //        return null;

        //    return DecryptValueBytes(row.dataValue, password);
        //}

        /// <summary>
        /// Get the value from a key/value pair saved on a player's deviceId encrypted with the given password. Expired entries will be ignored.
        /// </summary>
        /// <param name="playerId">the player-specific ID used. Expected to be a unique DeviceID to identify a phone, per that device's rules.</param>
        /// <param name="key">The key to load data from for the playerId.</param>
        /// <param name="password">The password used to encrypt the value originally.</param>
        /// <returns>The value saved to the key with the password given, or an empty string if no key/value pair was found or the password is incorrect.</returns>
        public static string GetSecurePlayerData(string playerId, string key, string password)
        {
            var db = new PraxisContext();
            var row = db.PlayerData.FirstOrDefault(p => p.DeviceID == playerId && p.DataKey == key);
            if (row == null || row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.Now)
                return "";
            return DecryptValue(row.DataValue, password);
        }

        /// <summary>
        /// Saves a key/value pair to a given player's DeviceID with a password.
        /// </summary>
        /// <param name="playerId">the unique ID for the player, expected to be their unique device ID</param>
        /// <param name="key">The key to save to the database. Keys are unique, and you cannot have multiples of the same key.</param>
        /// <param name="value">The value to save with the key.</param>
        /// <param name="password"></param>
        /// <param name="expiration">If not null, expire this data in this many seconds from now.</param>
        /// <returns>true if data was saved, false if data was not.</returns>
        public static bool SetSecurePlayerData(string playerId, string key, string value, string password, double? expiration = null)
        {
            string encryptedValue = EncryptValue(value, password);

            var db = new PraxisContext();
            //An upsert command would be great here, but I dont think the entities do that.
            var row = db.PlayerData.FirstOrDefault(p => p.DeviceID == playerId && p.DataKey == key);
            if (row == null)
            {
                row = new DbTables.PlayerData();
                row.DataKey = key;
                row.DeviceID = playerId;
                db.PlayerData.Add(row);
            }
            if (expiration.HasValue)
                row.Expiration = DateTime.Now.AddSeconds(expiration.Value);
            else
                row.Expiration = null;
            row.DataValue = encryptedValue;
            return db.SaveChanges() == 1;
        }

        /// <summary>
        /// Get the value from a key/value pair saved on a map element. Expired entries will be ignored.
        /// </summary>
        /// <param name="elementId">the Guid exposed to clients to identify the map element.</param>
        /// <param name="key">The key to load from the database. Keys are unique, and you cannot have multiples of the same key.</param>
        /// <returns>The value saved to the key, or an empty string if no key/value pair was found.</returns>
        public static string GetSecureElementData(Guid elementId, string key, string password)
        {
            var db = new PraxisContext();
            var row = db.CustomDataOsmElements.Include(p => p.StoredOsmElement).FirstOrDefault(p => p.StoredOsmElement.PrivacyId == elementId && p.DataKey == key);
            if (row == null || row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.Now)
                return "";
            return DecryptValue(row.DataValue, password);
        }

        /// <summary>
        /// Saves a key/value pair to a given map element with the given password
        /// </summary>
        /// <param name="elementId">the Guid exposed to clients to identify the map element.</param>
        /// <param name="key">The key to save to the database for the map element.</param>
        /// <param name="value">The value to save with the key.</param>
        /// <param name="password">The password to encrypt the value with.</param>
        /// <param name="expiration">If not null, expire this data in this many seconds from now.</param>
        /// <returns>true if data was saved, false if data was not.</returns>
        public static bool SetSecureElementData(Guid elementId, string key, string value,string password, double? expiration = null)
        {
            string encryptedValue = EncryptValue(value, password);
            var db = new PraxisContext();

            var row = db.CustomDataOsmElements.Include(p => p.StoredOsmElement).FirstOrDefault(p => p.StoredOsmElement.PrivacyId == elementId && p.DataKey == key);
            if (row == null)
            {
                var sourceItem = db.StoredOsmElements.First(p => p.PrivacyId == elementId);
                row = new DbTables.CustomDataOsmElement();
                row.DataKey = key;
                row.StoredOsmElement = sourceItem;
                db.CustomDataOsmElements.Add(row);
            }
            if (expiration.HasValue)
                row.Expiration = DateTime.Now.AddSeconds(expiration.Value);
            else
                row.Expiration = null;
            row.DataValue = encryptedValue;
            return db.SaveChanges() == 1;
        }

        private static string EncryptValue(string value, string password)
        {
            byte[] passwordBytes = password.ToByteArrayUnicode();
            byte[] iv = baseSec.IV; //This changes every run, so we do need to save this alongside the data itself.
            var crypter = baseSec.CreateEncryptor(passwordBytes, baseSec.IV);
            var ms = new MemoryStream();
            using (CryptoStream cs = new CryptoStream(ms, crypter, CryptoStreamMode.Write))
            using (StreamWriter sw = new StreamWriter(cs))
            {
                sw.Write(value);
            }

            var data = Convert.ToBase64String(iv) + "|" + Convert.ToBase64String(ms.ToArray());
            return data;
        }

        private static string EncryptValue(byte[] value, string password)
        {
            byte[] passwordBytes = password.ToByteArrayUnicode();
            byte[] iv = baseSec.IV; //This changes every run, so we do need to save this alongside the data itself.
            var crypter = baseSec.CreateEncryptor(passwordBytes, baseSec.IV);
            var ms = new MemoryStream();
            using (CryptoStream cs = new CryptoStream(ms, crypter, CryptoStreamMode.Write))
            using (StreamWriter sw = new StreamWriter(cs))
            {
                sw.Write(value);
            }

            var data = Convert.ToBase64String(iv) + "|" + Convert.ToBase64String(ms.ToArray());
            return data;
        }

        private static string DecryptValue(string value, string password)
        {
            var results = System.Text.Encoding.Unicode.GetString(DecryptValueBytes(value, password));
            return results;
        }

        private static byte[] DecryptValueBytes(string value, string password)
        {
            var spanVal = value.AsSpan();
            byte[] ivBytes = Convert.FromBase64String(spanVal.SplitNext('|').ToString());
            byte[] encrypedData = Convert.FromBase64String(spanVal.ToString());

            byte[] passwordBytes = password.ToByteArrayUnicode();
            var crypter = baseSec.CreateDecryptor(passwordBytes, ivBytes);

            var ms = new MemoryStream();
            using (CryptoStream cs = new CryptoStream(ms, crypter, CryptoStreamMode.Write))
                cs.Write(encrypedData);

            return ms.ToArray();
        }
    }
}
