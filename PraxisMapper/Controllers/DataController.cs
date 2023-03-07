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

        [HttpGet]
        [Route("/[controller]/GetPlayerData/{accountId}/{key}")]
        [Route("/[controller]/Player/{accountId}/{key}")]
        public void GetPlayerData(string accountId, string key) {
            var data = GenericData.GetPlayerData(accountId, key);
            Response.BodyWriter.Write(data);
            return;
        }

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

        [HttpGet]
        [Route("/[controller]/GetElementData/{elementId}/{key}")]
        [Route("/[controller]/Element/{elementId}/{key}")]
        [Route("/[controller]/Place/{elementId}/{key}")]
        public void GetElementData(Guid elementId, string key) {
            var data = GenericData.GetPlaceData(elementId, key);
            Response.BodyWriter.Write(data);
            return;
        }

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

        [HttpGet]
        [Route("/[controller]/GetGlobalData/{key}")]
        [Route("/[controller]/Global/{key}")]
        public void GetGlobalData(string key) {
            var data = GenericData.GetGlobalData(key);
            Response.BodyWriter.Write(data);
            return;
        }

        [HttpDelete]
        [Route("/[controller]/Global/{key}")]
        public void DeleteGlobalData(string key) {
            GenericData.SetGlobalData(key, "");
            return;
        }

        [HttpPut]
        [Route("/[controller]/IncrementPlayerData/{accountId}/{key}/{changeAmount}")]
        [Route("/[controller]/Player/Increment/{accountId}/{key}/{changeAmount}")]
        public void IncrementPlayerData(string accountId, string key, double changeAmount, double? expirationTimer = null) {
            GenericData.IncrementPlayerData(accountId, key, changeAmount, expirationTimer);
        }

        [HttpPut]
        [Route("/[controller]/IncrementGlobalData/{key}/{changeAmount}")]
        [Route("/[controller]/Global/Increment/{key}/{changeAmount}")]
        public void IncrementGlobalData(string key, double changeAmount) {
            GenericData.IncrementGlobalData(key, changeAmount);
        }

        [HttpPut]
        [Route("/[controller]/IncrementPlusCodeData/{plusCode}/{key}/{changeAmount}")]
        [Route("/[controller]/PlusCode/Increment/{plusCode}/{key}/{changeAmount}")]
        [Route("/[controller]/Area/Increment/{plusCode}/{key}/{changeAmount}")]
        public void IncrementPlusCodeData(string plusCode, string key, double changeAmount, double? expirationTimer = null) {
            if (!DataCheck.IsInBounds(plusCode))
                return;

            GenericData.IncrementAreaData(plusCode, key, changeAmount, expirationTimer);
        }

        [HttpPut]
        [Route("/[controller]/IncrementElementData/{elementId}/{key}/{changeAmount}")]
        [Route("/[controller]/Element/Increment/{elementId}/{key}/{changeAmount}")]
        [Route("/[controller]/Place/Increment/{elementId}/{key}/{changeAmount}")]
        public void IncrementElementData(Guid elementId, string key, double changeAmount, double? expirationTimer = null) {
            GenericData.IncrementPlaceData(elementId, key, changeAmount, expirationTimer);
        }

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

        public string GetTerrainDataNew(string plusCode) {
            //NOTE: plusCode has no plus here.
            //for individual Cell10 or Cell11 checks. Existing terrain calls only do Cell10s in a Cell8 or larger area.
            //Might be better in PraxisCore to be reused.

            var place = AreaStyle.GetSinglePlaceFromArea(plusCode);
            var name = TagParser.GetName(place);
            StringBuilder sb = new StringBuilder();
            sb.Append(plusCode).Append('|').Append(name).Append('|').Append(place.StyleName).Append('|').Append(place.PrivacyId);

            return sb.ToString();
        }

        [HttpGet]
        [Route("/[controller]/GetScoreForPlace/{elementId}")]
        [Route("/[controller]/Score/{elementId}")]
        public long GetScoreForPlace(Guid elementId) {
            return PraxisCore.GameTools.ScoreData.GetScoreForSinglePlace(elementId);
        }

        [HttpGet]
        [Route("/[controller]/GetDistanceToPlace/{elementId}/{lat}/{lon}")]
        [Route("/[controller]/Distance/{elementId}/{lat}/{lon}")]
        public double GetDistanceToPlace(Guid elementId, double lat, double lon) {
            var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var place = db.Places.FirstOrDefault(e => e.PrivacyId == elementId);
            if (place == null) return 0;
            return place.ElementGeometry.Distance(new NetTopologySuite.Geometries.Point(lon, lat));
        }

        [HttpGet]
        [Route("/[controller]/GetCenterOfPlace/{elementId}")]
        [Route("/[controller]/Center/{elementId}")]
        public string GetCenterOfPlace(Guid elementId) {
            var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var place = db.Places.FirstOrDefault(e => e.PrivacyId == elementId);
            if (place == null) return "0|0";
            var center = place.ElementGeometry.Centroid;
            return center.Y.ToString() + "|" + center.X.ToString();
        }
    }
}