using CoreComponents;
using Google.OpenLocationCode;
using Microsoft.AspNetCore.Mvc;
using PraxisMapper.Classes;
using System.Text;
using static CoreComponents.DbTables;
using static CoreComponents.Place;

namespace PraxisMapper.Controllers
{
    //DataController: For handling generic get/set commands for data by location.
    //The part that actually allows for games to be made using PraxisMapper without editing code. (give or take styles until a style editor exists on an admin interface)
    //If I wanted to track data per user on the server, this similar setup might be the way to do it, with phoneId, key, and value.

    [Route("[controller]")]
    [ApiController]
    public class DataController : Controller
    {
        static object playerIncrementLock = new object();
        static object globalIncrementLock = new object();
        static object plusCodeIncrementLock = new object();
        static object storedElementIncrementLock = new object();

        //TODO: make all Set* values a Put instead of a Get
        [HttpGet]
        [Route("/[controller]/SetPlusCodeData/{plusCode}/{key}/{value}")]
        public bool SetPlusCodeData(string plusCode, string key, string value)
        {
            return GenericData.SetPlusCodeData(plusCode, key, value);
        }

        [HttpGet]
        [Route("/[controller]/GetPlusCodeData/{plusCode}/{key}")]
        public string GetPlusCodeData(string plusCode, string key)
        {
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
        public bool SetStoredElementData(long elementId, string key, string value)
        {
            return GenericData.SetStoredElementData(elementId, key, value);
        }

        [HttpGet]
        [Route("/[controller]/GetElementData/{elementId}/{key}")]
        public string GetElementData(long elementId, string key)
        {
            return GenericData.GetElementData(elementId, key);
        }

        [HttpGet]
        [Route("/[controller]/GetAllDataInPlusCode/{plusCode}")]
        public string GetAllDataInPlusCode(string plusCode)
        {
            var data = GenericData.GetAllDataInPlusCode(plusCode);
            StringBuilder sb = new StringBuilder();
            foreach (var d in data)
                sb.Append(d.plusCode).Append("|").Append(d.key).Append("|").AppendLine(d.value);

            return sb.ToString();
        }

        [HttpGet]
        [Route("/[controller]/GetAllDataInOsmElement/{elementId}/{elementType}")]
        public string GetAllDataInOsmElement(long elementId, int elementType)
        {
            var db = new PraxisContext();

            var data = GenericData.GetAllDataInPlace(elementId, elementType);
            StringBuilder sb = new StringBuilder();
            foreach (var d in data)
                sb.Append(d.elementId).Append("|").Append(d.key).Append("|").AppendLine(d.value);

            return sb.ToString();
        }

        [HttpGet]
        [Route("/[controller]/DrawUserData/{styleSet}/{plusCode}")]
        public byte[] DrawCustomDataTile(string styleSet, string plusCode)
        {
            var styleToUse = TagParser.allStyleGroups[styleSet];
            //TODO: pull out generic data, draw it as appropriate.
            return null;
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
            //TODO: this could create and store the bounds entry ahead of time, possibly on startup
            //instead of getting it each call.
            var db = new PraxisContext();
            var bounds = db.ServerSettings.First();
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
        public void IncrementStoredElementData(long elementId, string key, double changeAmount)
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
        [Route("/MapData/LearnCell8/{plusCode}")]
        public string GetPlusCodeTerrainData(string plusCode, int fullCode = 1)
        {
            PerformanceTracker pt = new PerformanceTracker("GetPlusCodeTerrainData");
            var codeString = plusCode;
            GeoArea box = OpenLocationCode.DecodeValid(codeString);
            var places = GetPlaces(box); //, includeGenerated: Configuration.GetValue<bool>("generateAreas")  //All the places in this 8-code

            //TODO: restore the auto-generate interesting areas logic with v4. This will mean making areas if there's 0 IsGameElement values in the results, since it's all in 1 table now.
            //if (Configuration.GetValue<bool>("generateAreas") && !places.Any(p => p.AreaTypeId < 13 || p.AreaTypeId == 100)) //check for 100 to not make new entries in the same spot.
            //{
            //    var newAreas = CreateInterestingPlaces(codeString8);
            //    places = newAreas.Select(g => new MapData() { MapDataId = g.GeneratedMapDataId + 100000000, place = g.place, type = g.type, name = g.name, AreaTypeId = g.AreaTypeId }).ToList();
            //}

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(codeString);
            //pluscode8 //first 6 digits of this pluscode. each line below is the last 4 that have an area type.
            //pluscode2|name|type|MapDataID  //less data transmitted, an extra string concat per entry phone-side.

            var data = AreaTypeInfo.SearchArea(ref box, ref places, true); //TODO: do i need to string.join() the data.select() results below to get all entries in one spot?
            var results = String.Join(Environment.NewLine, data.Select(d => d.Key + "|" + d.Value.Select(v => v.Name + "|" + v.areaType + "|" + v.StoredOsmElementId).FirstOrDefault()));

            pt.Stop(codeString);
            return results;
        }

        [HttpGet]
        [Route("/[controller]/GetScoreForArea/{elementId}")]
        public long GetScoreForArea(long elementId)
        {
            return ScoreData.GetScoreForSinglePlace(elementId);
        }
    }
}