using CryptSharp;
using Google.OpenLocationCode;
using Microsoft.EntityFrameworkCore;
using PraxisCore.Support;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
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
        private const string systemPassword = "9ec44aa8-e8cc-4421-8724-0ca876c6ec73"; //Used to encrypt things that aren't player specific

        public static bool SetAreaData(string plusCode, string key, string value, double? expiration = null)
        {
            return SetAreaData(plusCode, key, value.ToByteArrayUTF8(), expiration);
        }

        public static bool SetAreaData(string plusCode, string key, byte[] value, double? expiration = null)
        {
            var db = new PraxisContext();
            if (db.PlayerData.Any(p => p.DeviceID == key))
                return false;

            if (value.Length < 128)
            {
                string valString = value.ToUTF8String();
                if (db.PlayerData.Any(p => p.DeviceID == valString))
                    return false;
            }

            var row = db.AreaGameData.FirstOrDefault(p => p.PlusCode == plusCode && p.DataKey == key);
            if (row == null)
            {
                row = new DbTables.AreaGameData();
                row.DataKey = key;
                row.PlusCode = plusCode;
                row.GeoAreaIndex = Converters.GeoAreaToPolygon(OpenLocationCode.DecodeValid(plusCode.ToUpper()));
                db.AreaGameData.Add(row);
            }
            if (expiration.HasValue)
                row.Expiration = DateTime.UtcNow.AddSeconds(expiration.Value);
            else
                row.Expiration = null;
            row.IvData = null;
            row.DataValue = value;
            db.SaveChangesAsync(); //TODO: determine if I want to do this change for performance and return the Task so the caller can check for errors, or leave it as it was.
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
            var db = new PraxisContext();
            var row = db.AreaGameData.FirstOrDefault(p => p.PlusCode == plusCode && p.DataKey == key);
            if (row == null || row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.UtcNow)
                return new byte[0];
            return row.DataValue;
        }

        /// <summary>
        /// Saves a key/value pair to a given map element. Will reject a pair containing a player's deviceId in the database.
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

        public static bool SetPlaceData(Guid elementId, string key, byte[] value, double? expiration = null)
        {
            var db = new PraxisContext();
            if (db.PlayerData.Any(p => p.DeviceID == key))
                return false;

            var row = db.PlaceGameData.Include(p => p.Place).FirstOrDefault(p => p.Place.PrivacyId == elementId && p.DataKey == key);
            if (row == null)
            {
                var sourceItem = db.Places.First(p => p.PrivacyId == elementId);
                row = new DbTables.PlaceGameData();
                row.DataKey = key;
                row.Place = sourceItem;
                db.PlaceGameData.Add(row);
            }
            if (expiration.HasValue)
                row.Expiration = DateTime.UtcNow.AddSeconds(expiration.Value);
            else
                row.Expiration = null;
            row.IvData = null;
            row.DataValue = value;
            return db.SaveChanges() == 1;
        }

        /// <summary>
        /// Get the value from a key/value pair saved on a map element. Expired entries will be ignored.
        /// </summary>
        /// <param name="elementId">the Guid exposed to clients to identify the map element.</param>
        /// <param name="key">The key to load from the database. Keys are unique, and you cannot have multiples of the same key.</param>
        /// <returns>The value saved to the key, or an empty byte[] if no key/value pair was found.</returns>
        public static byte[] GetPlaceData(Guid elementId, string key)
        {
            var db = new PraxisContext();
            var row = db.PlaceGameData.Include(p => p.Place).FirstOrDefault(p => p.Place.PrivacyId == elementId && p.DataKey == key);
            if (row == null || row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.UtcNow)
                return new byte[0];
            return row.DataValue;
        }

        /// <summary>
        /// Get the value from a key/value pair saved on a player's deviceId. Expired entries will be ignored.
        /// </summary>
        /// <param name="playerId">the player-specific ID used. Expected to be a unique DeviceID to identify a phone, per that device's rules.</param>
        /// <param name="key">The key to load data from for the playerId.</param>
        /// <returns>The value saved to the key, or an empty byte[] if no key/value pair was found.</returns>
        public static byte[] GetPlayerData(string playerId, string key)
        {
            var db = new PraxisContext();
            var row = db.PlayerData.FirstOrDefault(p => p.DeviceID == playerId && p.DataKey == key);
            if (row == null || row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.UtcNow)
                return new byte[0];
            return row.DataValue;
        }

        public static bool SetPlayerData(string playerId, string key, string value, double? expiration = null)
        {
            if (DataCheck.IsPlusCode(value)) //reject attaching Player to Area
                return false; 

            return SetPlayerData(playerId, key, value.ToByteArrayUTF8(), expiration);
        }

        /// <summary>
        /// Saves a key/value pair to a given player's DeviceID. Will reject a pair containing a PlusCode or map element Id.
        /// </summary>
        /// <param name="playerId"></param>
        /// <param name="key">The key to save to the database. Keys are unique, and you cannot have multiples of the same key.</param>
        /// <param name="value">The value to save with the key.</param>
        /// <param name="expiration">If not null, expire this data in this many seconds from now.</param>
        /// <returns>true if data was saved, false if data was not.</returns>
        public static bool SetPlayerData(string playerId, string key, byte[] value, double? expiration = null)
        {
            if (DataCheck.IsPlusCode(key)) //reject attaching Player to Area
                return false;

            var guidString = value.ToUTF8String();
            var db = new PraxisContext();
            Guid tempCheck = new Guid();
            if ((Guid.TryParse(key, out tempCheck) && db.Places.Any(osm => osm.PrivacyId == tempCheck))
                || (Guid.TryParse(guidString, out tempCheck) && db.Places.Any(osm => osm.PrivacyId == tempCheck)))
                return false; //reject attaching a player to a Place

            var row = db.PlayerData.FirstOrDefault(p => p.DeviceID == playerId && p.DataKey == key);
            if (row == null)
            {
                row = new DbTables.PlayerData();
                row.DataKey = key;
                row.DeviceID = playerId;
                db.PlayerData.Add(row);
            }
            if (expiration.HasValue)
                row.Expiration = DateTime.UtcNow.AddSeconds(expiration.Value);
            else
                row.Expiration = null;
            row.IvData = null;
            row.DataValue = value;
            
            return db.SaveChanges() == 1;
        }

        /// <summary>
        /// Load all of the key/value pairs in a PlusCode, including pairs saved to a longer PlusCode. Expired and encrypted entries are ignored.
        /// (EX: calling this with an 8 character PlusCode will load all contained 10 character PlusCodes key/value pairs)
        /// </summary>
        /// <param name="plusCode">A valid PlusCode, excluding the + symbol.</param>
        /// <param name="key">If supplied, only returns data on the given key for the area provided. If blank, returns all keys</param>
        /// <returns>a List of results with the PlusCode, keys, and values</returns>
        public static List<CustomDataAreaResult> GetAllDataInArea(string plusCode, string key = "")
        {
            //TODO: this throws an error about update locks can't be acquired in a READ UNCOMMITTED block or something when key != "" on MariaDB.
            var db = new PraxisContext();
            var plusCodeArea = OpenLocationCode.DecodeValid(plusCode);
            var plusCodePoly = Converters.GeoAreaToPolygon(plusCodeArea);
            var plusCodeData = db.AreaGameData.Where(d => plusCodePoly.Contains(d.GeoAreaIndex) && (key == "" || key == d.DataKey) && d.IvData == null)
                .ToList() //Required to run the next Where on the C# side
                .Where(row => row.Expiration.GetValueOrDefault(DateTime.MaxValue) > DateTime.UtcNow)
                .Select(d => new CustomDataAreaResult(d.PlusCode, d.DataKey, d.DataValue.ToUTF8String()))
                .ToList();

            return plusCodeData;
        }

        /// <summary>
        /// Load all of the key/value pairs attached to a map element. Expired entries will be ignored.
        /// </summary>
        /// <param name="elementId">the Guid exposed to clients to identify the map element.</param>
        /// /// <param name="key">If supplied, only returns data on the given key for the area provided. If blank, returns all keys</param>
        /// <returns>a List of results with the map element ID, keys, and values</returns>
        public static List<CustomDataPlaceResult> GetAllDataInPlace(Guid elementId, string key = "")
        {
            var db = new PraxisContext();
            var place = db.Places.First(s => s.PrivacyId == elementId);
            var data = db.PlaceGameData.Where(d => d.PlaceId == place.Id && (key == "" || d.DataKey == key) && d.IvData == null)
                .ToList() //Required to run the next Where on the C# side
                .Where(row => row.Expiration.GetValueOrDefault(DateTime.MaxValue) > DateTime.UtcNow)
                .Select(d => new CustomDataPlaceResult(place.PrivacyId, d.DataKey, d.DataValue.ToUTF8String()))
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
                .Where(row => row.Expiration.GetValueOrDefault(DateTime.MaxValue) > DateTime.UtcNow && row.IvData == null)
                .Select(d => new CustomDataPlayerResult(d.DeviceID, d.DataKey, d.DataValue.ToUTF8String()))
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
            var db = new PraxisContext();
            var row = db.GlobalDataEntries.FirstOrDefault(s => s.DataKey == key);
            if (row == null)
                return new byte[0];
            return row.DataValue;
        }

        public static bool SetGlobalData(string key, string value)
        {
            return SetGlobalData(key, value.ToByteArrayUTF8());
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

            var db = new PraxisContext();
            if (db.PlayerData.Any(p => p.DeviceID == key || p.DeviceID == valString))
                trackingPlayer = true;

            if (DataCheck.IsPlusCode(key) || DataCheck.IsPlusCode(valString))
                trackingLocation = true;

            Guid tempCheck = new Guid();
            if ((Guid.TryParse(key, out tempCheck) && db.Places.Any(osm => osm.PrivacyId == tempCheck))
                || (Guid.TryParse(valString, out tempCheck) && db.Places.Any(osm => osm.PrivacyId == tempCheck)))
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

        public static bool SetSecureAreaData(string plusCode, string key, string value, string password, double? expiration = null)
        {
            return SetSecureAreaData(plusCode, key, value.ToByteArrayUTF8(), password, expiration);
        }

        public static bool SetSecureAreaData(string plusCode, string key, byte[] value, string password, double? expiration = null)
        {
            var db = new PraxisContext();
            byte[] encryptedValue = EncryptValue(value, password, out byte[] IVs);

            var row = db.AreaGameData.FirstOrDefault(p => p.PlusCode == plusCode && p.DataKey == key);
            if (row == null)
            {
                row = new DbTables.AreaGameData();
                row.DataKey = key;
                row.PlusCode = plusCode;
                row.GeoAreaIndex = Converters.GeoAreaToPolygon(OpenLocationCode.DecodeValid(plusCode.ToUpper()));
                db.AreaGameData.Add(row);
            }
            if (expiration.HasValue)
                row.Expiration = DateTime.UtcNow.AddSeconds(expiration.Value);
            else
                row.Expiration = null;

            row.DataValue = encryptedValue;
            row.IvData = IVs;
            return db.SaveChanges() == 1;
        }

        /// <summary>
        /// Get the value from a key/value pair saved on a PlusCode with a password. Expired values will not be sent over.
        /// </summary>
        /// <param name="plusCode">A valid PlusCode, excluding the + symbol.</param>
        /// <param name="key">The key to load from the database on the PlusCode</param>
        /// <param name="password">The password used to encrypt the value originally.</param>
        /// <returns>The value saved to the key, or an empty string if no key/value pair was found or the password is incorrect.</returns>
        public static byte[] GetSecureAreaData(string plusCode, string key, string password)
        {
            var db = new PraxisContext();
            var row = db.AreaGameData.FirstOrDefault(p => p.PlusCode == plusCode && p.DataKey == key);
            if (row == null || row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.UtcNow)
                return new byte[0];

            return DecryptValue(row.IvData, row.DataValue, password);
        }

        /// <summary>
        /// Get the value from a key/value pair saved on a player's deviceId encrypted with the given password. Expired entries will be ignored.
        /// </summary>
        /// <param name="playerId">the player-specific ID used. Expected to be a unique DeviceID to identify a phone, per that device's rules.</param>
        /// <param name="key">The key to load data from for the playerId.</param>
        /// <param name="password">The password used to encrypt the value originally.</param>
        /// <returns>The value saved to the key with the password given, or an empty string if no key/value pair was found or the password is incorrect.</returns>
        public static byte[] GetSecurePlayerData(string playerId, string key, string password)
        {
            var db = new PraxisContext();
            var row = db.PlayerData.FirstOrDefault(p => p.DeviceID == playerId && p.DataKey == key);
            if (row == null || row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.UtcNow)
                return new byte[0];
            return DecryptValue(row.IvData, row.DataValue, password);
        }

        public static bool SetSecurePlayerData(string playerId, string key, string value, string password, double? expiration = null)
        {
            return SetSecurePlayerData(playerId, key, value.ToByteArrayUTF8(), password, expiration);
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
        public static bool SetSecurePlayerData(string playerId, string key, byte[] value, string password, double? expiration = null)
        {
            var encryptedValue = EncryptValue(value, password, out byte[] IVs);

            var db = new PraxisContext();
            var row = db.PlayerData.FirstOrDefault(p => p.DeviceID == playerId && p.DataKey == key);
            if (row == null)
            {
                row = new DbTables.PlayerData();
                row.DataKey = key;
                row.DeviceID = playerId;
                db.PlayerData.Add(row);
            }
            if (expiration.HasValue)
                row.Expiration = DateTime.UtcNow.AddSeconds(expiration.Value);
            else
                row.Expiration = null;
            row.IvData = IVs;
            row.DataValue = encryptedValue;
            return db.SaveChanges() == 1;
        }

        /// <summary>
        /// Get the value from a key/value pair saved on a map element. Expired entries will be ignored.
        /// </summary>
        /// <param name="elementId">the Guid exposed to clients to identify the map element.</param>
        /// <param name="key">The key to load from the database. Keys are unique, and you cannot have multiples of the same key.</param>
        /// <returns>The value saved to the key, or an empty string if no key/value pair was found.</returns>
        public static byte[] GetSecurePlaceData(Guid elementId, string key, string password)
        {
            var db = new PraxisContext();
            var row = db.PlaceGameData.Include(p => p.Place).FirstOrDefault(p => p.Place.PrivacyId == elementId && p.DataKey == key);
            if (row == null || row.Expiration.GetValueOrDefault(DateTime.MaxValue) < DateTime.UtcNow)
                return new byte[0];
            return DecryptValue(row.IvData, row.DataValue, password);
        }

        public static bool SetSecurePlaceData(Guid elementId, string key, string value, string password, double? expiration = null)
        {
            return SetSecurePlaceData(elementId, key, value.ToByteArrayUTF8(), password, expiration);
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
        public static bool SetSecurePlaceData(Guid elementId, string key, byte[] value,string password, double? expiration = null)
        {
            byte[] encryptedValue = EncryptValue(value, password, out byte[] IVs);
            var db = new PraxisContext();

            var row = db.PlaceGameData.Include(p => p.Place).FirstOrDefault(p => p.Place.PrivacyId == elementId && p.DataKey == key);
            if (row == null)
            {
                var sourceItem = db.Places.First(p => p.PrivacyId == elementId);
                row = new DbTables.PlaceGameData();
                row.DataKey = key;
                row.Place = sourceItem;
                db.PlaceGameData.Add(row);
            }
            if (expiration.HasValue)
                row.Expiration = DateTime.UtcNow.AddSeconds(expiration.Value);
            else
                row.Expiration = null;
            row.IvData = IVs;
            row.DataValue = encryptedValue;
            return db.SaveChanges() == 1;
        }

        private static byte[] EncryptValue(byte[] value, string password, out byte[] IVs)
        {
            byte[] passwordBytes = SHA256.HashData(password.ToByteArrayUTF8());
            baseSec.GenerateIV();
            IVs = baseSec.IV;
            var crypter = baseSec.CreateEncryptor(passwordBytes, baseSec.IV);

            var ms = new MemoryStream();
            using (CryptoStream cs = new CryptoStream(ms, crypter, CryptoStreamMode.Write))
                cs.Write(value, 0, value.Length);

            return ms.ToArray();
        }

        private static byte[] DecryptValue(byte[] IVs, byte[] value, string password)
        {
            byte[] passwordBytes = SHA256.HashData(password.ToByteArrayUTF8());
         
            var crypter = baseSec.CreateDecryptor(passwordBytes, IVs);

            var ms = new MemoryStream();
            using (CryptoStream cs = new CryptoStream(ms, crypter, CryptoStreamMode.Write))
                cs.Write(value);

            return ms.ToArray();
        }

        public static byte[] ReadBody(PipeReader br, int contentLength)
        {
            var rr = br.ReadAtLeastAsync(contentLength);
            var wait = rr.GetAwaiter();
            while (!wait.IsCompleted)
                System.Threading.Thread.Sleep(10);
            var endData = rr.Result.Buffer.ToArray();
            br.AdvanceTo(rr.Result.Buffer.Start); // this is required to silence an error in Kestrel on Linux.
            return endData;
        }

        public static bool EncryptPassword(string userId, string password, int rounds)
        {
            //TODO: I might want to set the IV column on this entry to some invalid entry, so that it doesnt show up in /All results.
            var options = new CrypterOptions() {
                { CrypterOption.Rounds, rounds}
            };
            BlowfishCrypter crypter = new BlowfishCrypter();
            var salt = crypter.GenerateSalt(options);
            var results = crypter.Crypt(password, salt);
            //GenericData.SetPlayerData(userId, "password", results);
            var db = new PraxisContext();
            var entry = db.AuthenticationData.Where(a => a.accountId == userId).FirstOrDefault();
            if (entry == null)
            {
                entry = new DbTables.AuthenticationData();
                db.AuthenticationData.Add(entry);
                entry.accountId = userId;
            }
            entry.password = results;
            db.SaveChanges();

            return true;
        }

        public static bool CheckPassword(string userId, string password)
        {
            BlowfishCrypter crypter = new BlowfishCrypter();
            var db = new PraxisContext();
            var entry = db.AuthenticationData.Where(a => a.accountId == userId).FirstOrDefault();
            if (entry == null)
                return false;
            string checkedPassword = crypter.Crypt(password, entry.password);
            return entry.password == checkedPassword;
        }
    }
}
