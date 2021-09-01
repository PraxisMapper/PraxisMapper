using CoreComponents;
using Google.OpenLocationCode;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using static CoreComponents.DbTables;

namespace PraxisMapper.Controllers
{
    //DataController: For handling generic get/set commands for data by location.
    //The part that actually allows for games to be made using PraxisMapper without editing code. (give or take styles until a style editor exists on an admin interface)
    //If I wanted to track data per user on the server, this similar setup might be the way to do it, with phoneId, key, and value.

    [Route("[controller]")]
    [ApiController]
    public class DataController : Controller
    {
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

    }
}