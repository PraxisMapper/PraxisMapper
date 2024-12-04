using Google.OpenLocationCode;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PraxisCore;
using System;
using System.Buffers;
using System.Linq;
using System.Text;
using static PraxisCore.Place;

namespace PraxisMapper.Controllers {
    //DataController: For handling generic get/set commands for data, possibly attached to a Player/Place/Area.
    //The part that actually allows for games to be made using PraxisMapper without editing code. (give or take styles until a style editor exists on an admin interface)

    [Route("[controller]")]
    [ApiController]
    public class DataController : Controller {
        private readonly IConfiguration Configuration;
        private static IMemoryCache cache;

        public DataController(IConfiguration configuration, IMemoryCache memoryCacheSingleton) {
            Configuration = configuration;
            cache = memoryCacheSingleton;
        }
        public override void OnActionExecuting(ActionExecutingContext context) {
            base.OnActionExecuting(context);
            if (Configuration.GetValue<bool>("enableDataEndpoints") == false)
                HttpContext.Abort();
        }

        /// <summary>
        /// Saves a key-value pair to the given PlusCode.
        /// </summary>
        /// <param name="plusCode">A PlusCode, of any valid size</param>
        /// <param name="key">The key to save the value under</param>
        /// <param name="value">The value to be saved</param>
        /// <param name="expiresIn">If provided, this is how many seconds after saving the key-value pair will be valid for</param>
        /// <returns>true if the value saved</returns>
        /// <remarks>Values are saved to the specific PlusCode provided, and will not return key-value pairs in parent or child areas.
        /// (EX: saving "test":"no" to "223344" will not return that value for GET calls to "2233" or "22334455").
        /// Expired data is not returned to clients, and is deleted from the database some time after expiration.
        /// For security purposes, do not attach identifying info on players with this call - Use the equivalent SecureData endpoint if you need to 
        /// save a player's account name, time visited, or other information that lets you figure out who was where at what time.</remarks>
        [HttpPut]
        [Route("/[controller]/SetPlusCodeData/{plusCode}/{key}/{value}")]
        [Route("/[controller]/SetPlusCodeData/{plusCode}/{key}/{value}/{expiresIn}")]
        [Route("/[controller]/PlusCode/{plusCode}/{key}/{value}")]
        [Route("/[controller]/PlusCode/{plusCode}/{key}/{value}/{expiresIn}")]
        [Route("/[controller]/Area/{plusCode}/{key}/")]
        [Route("/[controller]/Area/{plusCode}/{key}/{value}")]
        [Route("/[controller]/Area/{plusCode}/{key}/noval/{expiresIn}")]
        [Route("/[controller]/Area/{plusCode}/{key}/{value}/{expiresIn}")]

        public bool SetPlusCodeData(string plusCode, string key, string value, double? expiresIn = null) {
            SimpleLockable.PerformWithLock(plusCode + "-" + key, () =>
            {
                if (!DataCheck.IsInBounds(plusCode))
                    return;

                if (value == null)
                {
                    var endData = GenericData.ReadBody(Request.BodyReader, (int)Request.ContentLength);
                    GenericData.SetAreaData(plusCode, key, endData, expiresIn);
                    return;
                }
                GenericData.SetAreaData(plusCode, key, value, expiresIn);
            });
            return true;
        }

        /// <summary>
        /// Asks for a value attached to a key on a Plus Code.
        /// </summary>
        /// <param name="plusCode">The PlusCode to check data against</param>
        /// <param name="key">They key to pull,if present</param>
        /// <remarks>If the key is present, the value is written to the response body as a byte array.</remarks>
        [HttpGet]
        [Route("/[controller]/GetPlusCodeData/{plusCode}/{key}")]
        [Route("/[controller]/PlusCode/{plusCode}/{key}")]
        [Route("/[controller]/Area/{plusCode}/{key}")]
        public void GetPlusCodeData(string plusCode, string key) {
            if (!DataCheck.IsInBounds(plusCode))
                return;
            var data = GenericData.GetAreaData(plusCode, key);
            Response.BodyWriter.Write(data);
            return;
        }

        /// <summary>
        /// Save a key-value pair to a player's account name
        /// </summary>
        /// <param name="accountId">the account name to save against</param>
        /// <param name="key">The key to look up data with</param>
        /// <param name="value">The value to save</param>
        /// <param name="expiresIn">If provided, this is how many seconds after saving the key-value pair will be valid for</param>
        /// <returns>true if data was saved</returns>
        /// <remarks>This is intended to be used for basic game info on an account. Use the equivalent SecureData endpoint to save info that 
        /// you do not want sent in plaintext format like significant editable save data or location history.</remarks>
        [HttpPut]
        [Route("/[controller]/SetPlayerData/{accountId}/{key}/{value}")]
        [Route("/[controller]/SetPlayerData/{accountId}/{key}/{value}/{expiresIn}")]
        [Route("/[controller]/Player/{accountId}/{key}/")]
        [Route("/[controller]/Player/{accountId}/{key}/{value}")]
        [Route("/[controller]/Player/{accountId}/{key}/{value}/{expiresIn}")]
        public bool SetPlayerData(string accountId, string key, string value, double? expiresIn = null) {
            SimpleLockable.PerformWithLock(accountId + "-" + key, () =>
            {
                if (value == null)
                {
                    var endData = GenericData.ReadBody(Request.BodyReader, (int)Request.ContentLength);
                    GenericData.SetPlayerData(accountId, key, endData, expiresIn);
                    return;
                }
                GenericData.SetPlayerData(accountId, key, value, expiresIn);
            });
            return true;
        }

        /// <summary>
        /// Read a value from a key-value pair on an account
        /// </summary>
        /// <param name="accountId">the account name to pull the value from</param>
        /// <param name="key">the key to pull the value from</param>
        /// <remarks>If found, the value is written into the body of the response as a byte array. </remarks>
        [HttpGet]
        [Route("/[controller]/GetPlayerData/{accountId}/{key}")]
        [Route("/[controller]/Player/{accountId}/{key}")]
        public void GetPlayerData(string accountId, string key) {
            var data = GenericData.GetPlayerData(accountId, key);
            Response.BodyWriter.Write(data);
            return;
        }

        /// <summary>
        /// Saved a key-value pair to a Place tracked in the server.
        /// </summary>
        /// <param name="elementId">The privacyID (GUID) used to identify the place in this PraxisMapper instance.</param>
        /// <param name="key">The key to save data against</param>
        /// <param name="value">The value to save</param>
        /// <param name="expiresIn">If provided, this is how many seconds after saving the key-value pair will be valid for</param>
        /// <returns>true if data was saved.</returns>
        /// <remarks>Expired data is not returned to clients, and is eventually deleted from the database.
        /// Do not save information on players with this endpoint. Use the equivalent SecureData endpoint for info that can identify users.</remarks>
        [HttpPut]
        [Route("/[controller]/SetElementData/{elementId}/{key}/{value}")]
        [Route("/[controller]/SetElementData/{elementId}/{key}/{value}/{expiresIn}")]
        [Route("/[controller]/Element/{elementId}/{key}/{value}")]
        [Route("/[controller]/Element/{elementId}/{key}/{value}/{expiresIn}")]
        [Route("/[controller]/Place/{elementId}/{key}")]
        [Route("/[controller]/Place/{elementId}/{key}/{value}")]
        [Route("/[controller]/Place/{elementId}/{key}/{value}/{expiresIn}")]
        public bool SetStoredElementData(Guid elementId, string key, string value, double? expiresIn = null) {
            SimpleLockable.PerformWithLock(elementId + "-" + key, () =>
            {
                if (value == null)
                {
                    var endData = GenericData.ReadBody(Request.BodyReader, (int)Request.ContentLength);
                    GenericData.SetPlaceData(elementId, key, endData, expiresIn);
                    return;
                }
                GenericData.SetPlaceData(elementId, key, value, expiresIn);
            });
            return true;
        }

        /// <summary>
        /// Read a value from a key-value pair on a Place
        /// </summary>
        /// <param name="elementId">the privacyID(GUID) to pull the value from</param>
        /// <param name="key">the key to pull the value from</param>
        /// <remarks>If found, the value is written into the body of the response as a byte array. </remarks>
        [HttpGet]
        [Route("/[controller]/GetElementData/{elementId}/{key}")]
        [Route("/[controller]/Element/{elementId}/{key}")]
        [Route("/[controller]/Place/{elementId}/{key}")]
        public void GetElementData(Guid elementId, string key) {
            var data = GenericData.GetPlaceData(elementId, key);
            Response.BodyWriter.Write(data);
            return;
        }

        /// <summary>
        /// Get All key-value pairs on an account
        /// </summary>
        /// <param name="accountId">the account name to look up data on</param>
        /// <returns>A string of all key-values pairs on the account. Each key-value pair is returned with the format "accountID|key1|value1\n". Split on newlines, then split on | again to have all values separated</returns>
        /// <remarks>If there are secure values saved on the account, they will be returned in their encrypted format.</remarks>
        [HttpGet]
        [Route("/[controller]/GetAllPlayerData/{accountId}")]
        [Route("/[controller]/Player/All/{accountId}")]
        public string GetAllPlayerData(string accountId) {
            var data = GenericData.GetAllPlayerData(accountId);
            StringBuilder sb = new StringBuilder();
            foreach (var d in data)
                sb.Append(d.accountId).Append('|').Append(d.DataKey).Append('|').Append(d.DataValue.ToUTF8String()).Append('\n');

            return sb.ToString();
        }

        /// <summary>
        /// Get all key value pairs on the PlusCode and its contained child PlusCodes
        /// </summary>
        /// <param name="plusCode">The parent PlusCode to load data from</param>
        /// <param name="key">If provided, only loads entries with this key</param>
        /// <returns>A string of lines in "plusCode|key|value\n" format</returns>
        /// <remarks>If you call this with PlusCode "223344", this will return entries for "22334455" since it's contained, but not "2233" as those may
        /// apply only outside the given PlusCode area.
        /// If any of these values were encrypted with the SecureData endpoints, they will be returned in encrypted format.</remarks>
        [HttpGet]
        [Route("/[controller]/GetAllDataInPlusCode/{plusCode}")]
        [Route("/[controller]/PlusCode/All/{plusCode}")]
        [Route("/[controller]/Area/All/{plusCode}")]
        [Route("/[controller]/Area/All/{plusCode}/{key}")]
        public string GetAllPlusCodeData(string plusCode, string key = "") {
            if (!DataCheck.IsInBounds(plusCode))
                return "";
            var data = GenericData.GetAllDataInArea(plusCode, key);
            StringBuilder sb = new StringBuilder();
            foreach (var d in data)
                sb.Append(d.PlusCode).Append('|').Append(d.DataKey).Append('|').Append(d.DataValue.ToUTF8String()).Append('\n');

            return sb.ToString();
        }

        /// <summary>
        /// Get all key-value pairs on a Place
        /// </summary>
        /// <param name="elementId"> the privacyID(GUID) for the place in question</param>
        /// <returns>A string of lines in "privacyID|key|value\n" format</returns>
        /// <remarks>If any of the key value pairs are encrypted, they will be returned in encryped format.</remarks>
        [HttpGet]
        [Route("/[controller]/GetAllDataInElement/{elementId}/")]
        [Route("/[controller]/Element/All/{elementId}/")]
        [Route("/[controller]/Place/All/{elementId}/")]
        public string GetAllPlaceData(Guid elementId) {
            var data = GenericData.GetAllDataInPlace(elementId);
            StringBuilder sb = new StringBuilder();
            foreach (var d in data)
                sb.Append(elementId).Append('|').Append(d.DataKey).Append('|').Append(d.DataValue.ToUTF8String()).Append('\n');

            return sb.ToString();
        }

        /// <summary>
        /// Set a global key-value pair
        /// </summary>
        /// <param name="key">the key to save data to</param>
        /// <param name="value">the value to save</param>
        /// <returns>true if the value was saved</returns>
        /// <remarks>If you do not pass value in on the URL, it will be read from the body instead.
        /// Global values here cannot be set to expire, and do not have a SecureData equivalent. They are meant to be universal.</remarks>
        [HttpPut]
        [Route("/[controller]/SetGlobalData/{key}/{value}")]
        [Route("/[controller]/Global/{key}")]
        [Route("/[controller]/Global/{key}/{value}")]
        public bool SetGlobalData(string key, string value) {
            SimpleLockable.PerformWithLock("global-" + key, () =>
            {
                if (value == null)
                {
                    var endData = GenericData.ReadBody(Request.BodyReader, (int)Request.ContentLength);
                    GenericData.SetGlobalData(key, endData);
                    return;
                }
                GenericData.SetGlobalData(key, value);
            });
            return true;
        }

        /// <summary>
        /// Read a global key-value pair
        /// </summary>
        /// <param name="key">the key to load data from</param>
        /// <remarks>This is intended for values that might change but are needed by all clients. Examples might include a message of the day,
        /// flags to indicate active events, game values that may change for balance reasons, etc.</remarks>
        [HttpGet]
        [Route("/[controller]/GetGlobalData/{key}")]
        [Route("/[controller]/Global/{key}")]
        public void GetGlobalData(string key) {
            var data = GenericData.GetGlobalData(key);
            Response.BodyWriter.Write(data);
            return;
        }

        /// <summary>
        /// Delete a globaly key-value pair
        /// </summary>
        /// <param name="key">the key to delete</param>
        /// <remarks>Actually sets the value to a blank string, which is the same response as the key not existing.</remarks>
        [HttpDelete]
        [Route("/[controller]/Global/{key}")]
        public void DeleteGlobalData(string key) {
            GenericData.SetGlobalData(key, "");
            return;
        }

        /// <summary>
        /// Increments the value in a key-value pair on an account
        /// </summary>
        /// <param name="accountId">the account name to find the value on</param>
        /// <param name="key">the key to find the value on</param>
        /// <param name="changeAmount">how much to increment the value by</param>
        /// <param name="expirationTimer">if set, the value is only valid for this many seconds after saving.</param>
        /// <remarks>Expired data is not returned to clients, and is eventually deleted from the database.
        /// This endpoint specifically locks the value while changing it, so discrete changes done in any order will return the same result.</remarks>
        [HttpPut]
        [Route("/[controller]/IncrementPlayerData/{accountId}/{key}/{changeAmount}")]
        [Route("/[controller]/Player/Increment/{accountId}/{key}/{changeAmount}")]
        public void IncrementPlayerData(string accountId, string key, double changeAmount, double? expirationTimer = null) {
            GenericData.IncrementPlayerData(accountId, key, changeAmount, expirationTimer);
        }

        /// <summary>
        /// Increments the value in a global key-value pair
        /// </summary>
        /// <param name="key">the key to find the value on</param>
        /// <param name="changeAmount">how much to increment the value by</param>
        /// <remarks>This endpoint specifically locks the value while changing it, so discrete changes done in any order will return the same result.</remarks>
        [HttpPut]
        [Route("/[controller]/IncrementGlobalData/{key}/{changeAmount}")]
        [Route("/[controller]/Global/Increment/{key}/{changeAmount}")]
        public void IncrementGlobalData(string key, double changeAmount) {
            GenericData.IncrementGlobalData(key, changeAmount);
        }

        /// <summary>
        /// Increments the value in a key-value pair on a PlusCode
        /// </summary>
        /// <param name="plusCode">the PlusCode to find the value on</param>
        /// <param name="key">the key to find the value on</param>
        /// <param name="changeAmount">how much to increment the value by</param>
        /// <param name="expirationTimer">if set, the value is only valid for this many seconds after saving.</param>
        /// <remarks>Expired data is not returned to clients, and is eventually deleted from the database.
        /// This endpoint specifically locks the value while changing it, so discrete changes done in any order will return the same result.</remarks>
        [HttpPut]
        [Route("/[controller]/IncrementPlusCodeData/{plusCode}/{key}/{changeAmount}")]
        [Route("/[controller]/PlusCode/Increment/{plusCode}/{key}/{changeAmount}")]
        [Route("/[controller]/Area/Increment/{plusCode}/{key}/{changeAmount}")]
        public void IncrementPlusCodeData(string plusCode, string key, double changeAmount, double? expirationTimer = null) {
            if (!DataCheck.IsInBounds(plusCode))
                return;

            GenericData.IncrementAreaData(plusCode, key, changeAmount, expirationTimer);
        }

        /// <summary>
        /// Increments the value in a key-value pair on a Place
        /// </summary>
        /// <param name="elementId">the privacyID(GUID) to find the value on</param>
        /// <param name="key">the key to find the value on</param>
        /// <param name="changeAmount">how much to increment the value by</param>
        /// <param name="expirationTimer">if set, the value is only valid for this many seconds after saving.</param>
        /// <remarks>Expired data is not returned to clients, and is eventually deleted from the database.
        /// This endpoint specifically locks the value while changing it, so discrete changes done in any order will return the same result.</remarks>
        [HttpPut]
        [Route("/[controller]/IncrementElementData/{elementId}/{key}/{changeAmount}")]
        [Route("/[controller]/Element/Increment/{elementId}/{key}/{changeAmount}")]
        [Route("/[controller]/Place/Increment/{elementId}/{key}/{changeAmount}")]
        public void IncrementElementData(Guid elementId, string key, double changeAmount, double? expirationTimer = null) {
            GenericData.IncrementPlaceData(elementId, key, changeAmount, expirationTimer);
        }

        /// <summary>
        /// Get the StyleSet match (terrain) for each Cell10 inside the given PlusCode
        /// </summary>
        /// <param name="plusCode">The PlusCode to read through. Must be 8 or fewer characters</param>
        /// <param name="styleSet">The StyleSet to use when reading place types. Defaults to 'mapTiles'.</param>
        /// <returns>A list of lines, 1 for each Cell10, in "plusCode|name|type|privacyID" format.</returns>
        /// <remarks>The smallest item in each Cell10 determines what the match for that Cell10 is.
        /// This is probably best done once and cached on the client, instead of called repeatedly, as it's a little resource-heavy.
        /// The client app may be able to do this faster if it has some access to the source data as well.
        /// </remarks>
        [HttpGet]
        [Route("/[controller]/GetPlusCodeTerrainData/{plusCode}")]
        [Route("/[controller]/Terrain/{plusCode}")]
        [Route("/[controller]/Terrain/{plusCode}/{styleSet}")]
        public string GetPlusCodeTerrainData(string plusCode, string styleSet = "mapTiles") {
            if (cache.TryGetValue("Terrain" + plusCode, out string cachedResults))
                return cachedResults;

            //This function returns 1 line per Cell10, the smallest (and therefore highest priority) item intersecting that cell10.
            GeoArea box = OpenLocationCode.DecodeValid(plusCode);
            if (!DataCheck.IsInBounds(box))
                return "";
            var places = GetPlaces(box, styleSet: styleSet);
            places = places.Where(p => p.StyleName != TagParser.defaultStyle.Name).ToList();

            StringBuilder sb = new StringBuilder();
            //pluscode|name|type|PrivacyID 

            var data = AreaStyle.GetAreaDetails(ref box, ref places);
            foreach (var d in data)
                sb.Append(d.plusCode).Append('|').Append(d.data.name).Append('|').Append(d.data.style).Append('|').Append(d.data.privacyId).Append('\n');
            var results = sb.ToString();
            cache.Set("Terrain" + plusCode, results, new TimeSpan(0, 0, 30));
            return results;
        }

        /// <summary>
        /// Get all StyleSet matches (terrain) for all items in each Cell10 inside the given PlusCode
        /// </summary>
        /// <param name="plusCode">The PlusCode to read through. Must be 8 or fewer characters</param>
        /// <param name="styleSet">The StyleSet to use when reading place types. Defaults to 'mapTiles'.</param>
        /// <returns>A list of lines, 1 for each match in each Cell10, in "plusCode|name|type|privacyID" format.</returns>
        /// <remarks>The smallest item in each Cell10 determines what the match for that Cell10 is.
        /// This is probably best done once and cached on the client, instead of called repeatedly, as it's a little resource-heavy.
        /// The client app may be able to do this faster if it has some access to the source data as well.
        /// </remarks>
        [HttpGet]
        [Route("/[controller]/GetPlusCodeTerrainDataFull/{plusCode}")]
        [Route("/[controller]/Terrain/All/{plusCode}")]
        [Route("/[controller]/Terrain/All/{plusCode}/{styleSet}")]
        public string GetPlusCodeTerrainDataFull(string plusCode, string styleSet = "mapTiles") {
            if (cache.TryGetValue("TerrainAll" + plusCode, out string cachedResults))
                return cachedResults;
            //This function returns 1 line per Cell10 per intersecting element. For an app that needs to know all things in all points.
            GeoArea box = OpenLocationCode.DecodeValid(plusCode);
            if (!DataCheck.IsInBounds(box))
                return "";
            var places = GetPlaces(box, styleSet: styleSet); //All the places in this Cell8
            places = places.Where(p => p.StyleName != TagParser.defaultStyle.Name).ToList();

            StringBuilder sb = new StringBuilder();
            //pluscode|name|type|privacyID

            var data = AreaStyle.GetAreaDetailsAll(ref box, ref places);
            foreach (var d in data)
                foreach (var v in d.data)
                    sb.Append(d.plusCode).Append('|').Append(v.name).Append('|').Append(v.style).Append('|').Append(v.privacyId).Append('\n');
            var results = sb.ToString();
            cache.Set("TerrainAll" + plusCode, results, new TimeSpan(0, 0, 30));
            return results;
        }

        /// <summary>
        /// Get the default 'Score' for a place.
        /// </summary>
        /// <param name="elementId">the PrivacyID of a Place to be scored</param>
        /// <returns>the score for the requested Place</returns>
        /// <remarks>The 'score' value is the number of Cell10s that fit inside the area of a Place that has an area,
        /// OR the length of a LineString divided by the Cell10 width for Places that are only lines,
        /// OR 1 for Places that are a single geometric Point on the map.</remarks>
        [HttpGet]
        [Route("/[controller]/GetScoreForPlace/{elementId}")]
        [Route("/[controller]/Score/{elementId}")]
        public long GetScoreForPlace(Guid elementId) {
            return PraxisCore.GameTools.ScoreData.GetScoreForSinglePlace(elementId);
        }

        /// <summary>
        /// Gets the distance from a lat/lon coordinate to a given place.
        /// </summary>
        /// <param name="elementId">the privacyID(GUID) of the place</param>
        /// <param name="lat">the latitude to calculate from</param>
        /// <param name="lon">the longitude to calculate from</param>
        /// <returns>the minimum distance, in degrees, to the given place.</returns>
        /// <remarks>a value of 0 indicates the place was not found. 
        /// The client may want to multiply this value by ConstantValues.metersPerDegree (111,111) to get a usable estimate in meters.</remarks>
        [HttpGet]
        [Route("/[controller]/GetDistanceToPlace/{elementId}/{lat}/{lon}")]
        [Route("/[controller]/Distance/{elementId}/{lat}/{lon}")]
        public double GetDistanceToPlace(Guid elementId, double lat, double lon) {
            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var place = db.Places.FirstOrDefault(e => e.PrivacyId == elementId);
            if (place == null) return 0;
            return place.ElementGeometry.Distance(new NetTopologySuite.Geometries.Point(lon, lat));
        }

        /// <summary>
        /// Return the geometric center of a given place.
        /// </summary>
        /// <param name="elementId">the privacyID(GUID) of the place to find the center of</param>
        /// <returns>a string with the center coordinates in "lat|lon" format</returns>
        /// <remarks>a value of "0|0" indicates the place was not found.</remarks>
        [HttpGet]
        [Route("/[controller]/GetCenterOfPlace/{elementId}")]
        [Route("/[controller]/Center/{elementId}")]
        public string GetCenterOfPlace(Guid elementId) {
            using var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var place = db.Places.FirstOrDefault(e => e.PrivacyId == elementId);
            if (place == null) return "0|0";
            var center = place.ElementGeometry.Centroid;
            return center.Y.ToString() + "|" + center.X.ToString();
        }
    }
}