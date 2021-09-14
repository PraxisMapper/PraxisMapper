using CoreComponents;
using Google.OpenLocationCode;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using NetTopologySuite.Geometries.Prepared;
using PraxisMapper.Classes;
using System.Text;
using static CoreComponents.DbTables;
using static CoreComponents.Place;

namespace PraxisMapper.Controllers
{
    //DataController: For handling generic get/set commands for data by location.
    //The part that actually allows for games to be made using PraxisMapper without editing code. (give or take styles until a style editor exists on an admin interface)

    [Route("[controller]")]
    [ApiController]
    public class DataController : Controller
    {
        static object playerIncrementLock = new object();
        static object globalIncrementLock = new object();
        static object plusCodeIncrementLock = new object();
        static object storedElementIncrementLock = new object();

        private readonly IConfiguration Configuration;
        private static IMemoryCache cache;

        public DataController(IConfiguration configuration, IMemoryCache memoryCacheSingleton)
        {
            Configuration = configuration;
            cache = memoryCacheSingleton;
        }

        //TODO: make all Set* values a Put instead of a Get
        [HttpGet]
        [Route("/[controller]/SetPlusCodeData/{plusCode}/{key}/{value}")]
        public bool SetPlusCodeData(string plusCode, string key, string value)
        {
            if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), OpenLocationCode.DecodeValid(plusCode)))
                return false;
            return GenericData.SetPlusCodeData(plusCode, key, value);
        }

        [HttpGet]
        [Route("/[controller]/GetPlusCodeData/{plusCode}/{key}")]
        public string GetPlusCodeData(string plusCode, string key)
        {
            if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), OpenLocationCode.DecodeValid(plusCode)))
                return "";
            return GenericData.GetPlusCodeData(plusCode, key);
        }

        //TODO: make all Set* values a Put instead of a Get
        [HttpGet]
        [Route("/[controller]/SetPlayerData/{deviceId}/{key}/{value}")]
        public bool SetPlayerData(string deviceId, string key, string value)
        {
            return GenericData.SetPlayerData(deviceId, key, value);
        }

        [HttpGet]
        [Route("/[controller]/GetPlayerData/{deviceId}/{key}")]
        public string GetPlayerData(string deviceId, string key)
        {
            return GenericData.GetPlayerData(deviceId, key);
        }

        [HttpGet]
        [Route("/[controller]/SetElementData/{elementId}/{key}/{value}")]
        public bool SetStoredElementData(Guid elementId, string key, string value)
        {
            return GenericData.SetStoredElementData(elementId, key, value);
        }

        [HttpGet]
        [Route("/[controller]/GetElementData/{elementId}/{key}")]
        public string GetElementData(Guid elementId, string key)
        {
            return GenericData.GetElementData(elementId, key);
        }

        [HttpGet]
        [Route("/[controller]/GetAllDataInPlusCode/{plusCode}")]
        public string GetAllDataInPlusCode(string plusCode)
        {
            if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), OpenLocationCode.DecodeValid(plusCode)))
                return "";
            var data = GenericData.GetAllDataInPlusCode(plusCode);
            StringBuilder sb = new StringBuilder();
            foreach (var d in data)
                sb.Append(d.plusCode).Append("|").Append(d.key).Append("|").AppendLine(d.value);

            return sb.ToString();
        }

        //[HttpGet]
        //[Route("/[controller]/GetAllDataInOsmElement/{elementId}/{elementType}")]
        //public string GetAllDataInOsmElement(long elementId, int elementType)

        //{
        //    var data = GenericData.GetAllDataInPlace(elementId, elementType);
        //    StringBuilder sb = new StringBuilder();
        //    foreach (var d in data)
        //        sb.Append(d.elementId).Append("|").Append(d.key).Append("|").AppendLine(d.value);

        //    return sb.ToString();
        //}

        [HttpGet]
        [Route("/[controller]/GetAllDataInOsmElement/{elementId}/")]
        public string GetAllDataInOsmElement(Guid elementId)

        {
            var data = GenericData.GetAllDataInPlace(elementId);
            StringBuilder sb = new StringBuilder();
            foreach (var d in data)
                sb.Append(d.elementId).Append("|").Append(d.key).Append("|").AppendLine(d.value);

            return sb.ToString();
        }

        [HttpGet]
        [Route("/[controller]/SetGlobalData/{key}/{value}")]
        public bool SetGlobalData(string key, string value)
        {
            return GenericData.SetGlobalData(key, value);
        }

        [HttpGet]
        [Route("/[controller]/GetGlobalData/{key}")]
        public string GetGlobalData(string key)
        {
            return GenericData.GetGlobalData(key);
        }

        [HttpGet]
        [Route("/[controller]/GetServerBounds")]
        public string GetServerBounds()
        {
            var bounds = cache.Get<ServerSetting>("settings");
            return bounds.SouthBound + "|" + bounds.WestBound + "|" + bounds.NorthBound + "|" + bounds.EastBound;
        }

        [HttpGet]
        [Route("/[controller]/IncrementPlayerData/{deviceId}/{key}/{changeAmount}")]
        public void IncrementPlayerData(string deviceId, string key, double changeAmount)
        {
            lock (playerIncrementLock)
            {
                var data = GenericData.GetPlayerData(deviceId, key);
                double val = 0;
                Double.TryParse(data, out val);
                val += changeAmount;
                GenericData.SetPlayerData(deviceId, key, val.ToString());
            }
        }

        [HttpGet]
        [Route("/[controller]/IncrementGlobalData/{key}/{changeAmount}")]
        public void IncrementGlobalData(string key, double changeAmount)
        {
            lock (globalIncrementLock)
            {
                var data = GenericData.GetGlobalData(key);
                double val = 0;
                Double.TryParse(data, out val);
                val += changeAmount;
                GenericData.SetGlobalData(key, val.ToString());
            }
        }

        [HttpGet]
        [Route("/[controller]/IncrementPlusCodeData/{plusCode}/{key}/{changeAmount}")]
        public void IncrementPlusCodeData(string plusCode, string key, double changeAmount)
        {
            if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), OpenLocationCode.DecodeValid(plusCode)))
                return;
            lock (plusCodeIncrementLock)
            {
                var data = GenericData.GetPlusCodeData(plusCode, key);
                double val = 0;
                Double.TryParse(data, out val);
                val += changeAmount;
                GenericData.SetPlusCodeData(plusCode, key, val.ToString());
            }
        }

        [HttpGet]
        [Route("/[controller]/IncrementStoredElementData/{elementId}/{key}/{changeAmount}")]
        public void IncrementStoredElementData(Guid elementId, string key, double changeAmount)
        {
            lock (storedElementIncrementLock)
            {
                var data = GenericData.GetElementData(elementId,key);
                double val = 0;
                Double.TryParse(data, out val);
                val += changeAmount;
                GenericData.SetStoredElementData(elementId, key, val.ToString());
            }
        }

        [HttpGet]
        [Route("/[controller]/GetPlusCodeTerrainData/{plusCode}")]
        public string GetPlusCodeTerrainData(string plusCode)
        {
            //This function returns 1 line per Cell10, the smallest (and therefore highest priority) item intersecting that cell10.
            PerformanceTracker pt = new PerformanceTracker("GetPlusCodeTerrainData");
            GeoArea box = OpenLocationCode.DecodeValid(plusCode);
            if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), box))
                return "";            
            var places = GetPlaces(box); 

            StringBuilder sb = new StringBuilder();
            //pluscode|name|type|StoredOsmElementID  //less data transmitted, an extra string concat per entry phone-side.

            var data = AreaTypeInfo.SearchArea(ref box, ref places);
            foreach (var d in data)
                sb.Append(d.Key).Append("|").Append(d.Value.Name).Append("|").Append(d.Value.areaType).Append("|").Append(d.Value.StoredOsmElementId).Append("\r\n");
            var results = sb.ToString();
            pt.Stop(plusCode);
            return results;
        }

        [HttpGet]
        [Route("/[controller]/GetPlusCodeTerrainDataFull/{plusCode}")]
        public string GetPlusCodeTerrainDataFull(string plusCode)
        {
            //This function returns 1 line per Cell10 per intersecting element. For an app that needs to know all things in all points.
            PerformanceTracker pt = new PerformanceTracker("GetPlusCodeTerrainDataFull");
            GeoArea box = OpenLocationCode.DecodeValid(plusCode);
            if (!DataCheck.IsInBounds(cache.Get<IPreparedGeometry>("serverBounds"), box))
                return "";
            var places = GetPlaces(box); //, includeGenerated: Configuration.GetValue<bool>("generateAreas")  //All the places in this Cell8

            StringBuilder sb = new StringBuilder();
            //pluscode|name|type|StoredOsmElementID

            var data = AreaTypeInfo.SearchAreaFull(ref box, ref places);
            foreach (var d in data)
                foreach(var v in d.Value)
                    sb.Append(d.Key).Append("|").Append(v.Name).Append("|").Append(v.areaType).Append("|").Append(v.StoredOsmElementId).Append("\r\n");
            var results = sb.ToString();
            pt.Stop(plusCode);
            return results;
        }

        [HttpGet]
        [Route("/[controller]/GetScoreForArea/{elementId}")]
        public long GetScoreForArea(Guid elementId)
        {
            return ScoreData.GetScoreForSinglePlace(elementId);
        }
    }
}