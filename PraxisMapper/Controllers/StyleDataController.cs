using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
            return data.MatchOrder + "|" + data.name + "|" + data.IsGameElement + "|" + data.minDrawRes + "|" + data.maxDrawRes + "|" + data.id;
        }

        [HttpGet]
        [Route("/[controller]/json/{styleSet}/")]
        public JsonResult GetStyleJson(string styleSet)
        {
            var db = new PraxisContext();
            JsonOptions jo = new JsonOptions();
            jo.JsonSerializerOptions.IncludeFields = true;
            jo.JsonSerializerOptions.MaxDepth = 2;
            var data = db.TagParserEntries.Include("TagParserMatchRules").Include("paintOperations").Where(t => t.styleSet == styleSet).ToList();
            var returnData = data.Select(x => new { 
                x.id, 
                x.name, 
                x.minDrawRes, 
                x.maxDrawRes, 
                x.styleSet, 
                x.IsGameElement, 
                x.MatchOrder, 
                paintOperations = x.paintOperations.Select(po => new { po.fileName, po.FillOrStroke, po.fromTag, po.HtmlColorCode, po.id, po.layerId, po.LinePattern, po.LineWidth, po.maxDrawRes, po.minDrawRes, po.randomize }).ToList(),
                TagParserMatchRules = x.TagParserMatchRules.Select(mr => new { mr.id, mr.Key, mr.MatchType, mr.Value}).ToList()  
            });
            return Json(returnData);
        }

        [HttpGet]
        [Route("/[controller]/UpdateStyleSetEntryValues/{styleSet}/{id}/{matchOrder}/{entryName}/{isGameElement}/{minDrawRes}/{maxDrawRes}")]
        public void UpdateStyleSetEntryValues(string styleSet, long id, int matchOrder, string entryName, bool isGameElement, double minDrawRes, double maxDrawRes)
        {
            //Hasnt yet been tested.
            var db = new PraxisContext();
            var data = db.TagParserEntries.FirstOrDefault(t => t.id == id);
            data.MatchOrder = matchOrder;
            data.name = entryName;
            data.IsGameElement = isGameElement;
            data.minDrawRes = minDrawRes;
            data.maxDrawRes = maxDrawRes;
            db.SaveChanges();
        }
    }
}
