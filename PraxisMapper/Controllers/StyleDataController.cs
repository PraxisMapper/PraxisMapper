using Microsoft.AspNetCore.Mvc;
using PraxisCore;
using System.Linq;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class StyleDataController : Controller
    {
        [HttpGet]
        [Route("/[controller]/GetStyleSetEntryNames/{styleSet}")]
        public string GetStyleSetEntryNames(string styleSet)
        {
            var db = new PraxisContext();
            var data = db.TagParserEntries.Where(t => t.styleSet == styleSet).OrderBy(t => t.MatchOrder).Select(t => t.name).ToList();
            return string.Join('|', data);
        }

        //These 2 functions do need to use IDs in case names change.
        [HttpGet]
        [Route("/[controller]/GetStyleSetEntryValues/{styleSet}/{entryName}")]
        public string GetStyleSetEntryValues(string styleSet, string entryName)
        {
            var db = new PraxisContext();
            var data = db.TagParserEntries.FirstOrDefault(t => t.styleSet == styleSet && t.name == entryName);
            return data.MatchOrder + "|" + data.name + "|" + data.IsGameElement + "|" + data.minDrawRes + "|" + data.maxDrawRes;
        }

        [HttpGet]
        [Route("/[controller]/UpdateStyleSetEntryValues/{styleSet}/{matchOrder}/{entryName}/{isGameElement}/{minDrawRes}/{maxDrawRes}")]
        public void UpdateStyleSetEntryValues(string styleSet, int matchOrder, string entryName, bool isGameElement, double minDrawRes, double maxDrawRes)
        {
            //Hasnt yet been tested.
            var db = new PraxisContext();
            var data = db.TagParserEntries.FirstOrDefault(t => t.styleSet == styleSet && t.name == entryName);
            data.MatchOrder = matchOrder;
            data.name = entryName;
            data.IsGameElement = isGameElement;
            data.minDrawRes = minDrawRes;
            data.maxDrawRes = maxDrawRes;
            db.SaveChanges();
        }
    }
}
