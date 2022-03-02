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
            var data = db.TagParserEntries.Where(t => t.StyleSet == styleSet).OrderBy(t => t.MatchOrder).Select(t => t.Name).ToList();
            return string.Join('|', data);
        }

        //These 2 functions do need to use IDs in case names change.
        [HttpGet]
        [Route("/[controller]/GetStyleSetEntryValues/{styleSet}/{entryName}")]
        public string GetStyleSetEntryValues(string styleSet, string entryName)
        {
            var db = new PraxisContext();
            var data = db.TagParserEntries.FirstOrDefault(t => t.StyleSet == styleSet && t.Name == entryName);
            return data.MatchOrder + "|" + data.Name + "|" + data.IsGameElement + "|" +  data.Id;
        }

        [HttpGet]
        [Route("/[controller]/json/{styleSet}/")]
        public JsonResult GetStyleJson(string styleSet)
        {
            var db = new PraxisContext();
            JsonOptions jo = new JsonOptions();
            jo.JsonSerializerOptions.IncludeFields = true;
            jo.JsonSerializerOptions.MaxDepth = 2;
            var data = db.TagParserEntries.Include("TagParserMatchRules").Include("paintOperations").Where(t => t.StyleSet == styleSet).ToList();
            var returnData = data.Select(x => new { 
                x.Id, 
                x.Name, 
                x.StyleSet, 
                x.IsGameElement, 
                x.MatchOrder, 
                paintOperations = x.PaintOperations.Select(po => new { po.FileName, po.FillOrStroke, po.FromTag, po.HtmlColorCode, po.Id, po.LayerId, po.LinePattern, po.LineWidth, po.MaxDrawRes, po.MinDrawRes, po.Randomize }).ToList(),
                TagParserMatchRules = x.TagParserMatchRules.Select(mr => new { mr.Id, mr.Key, mr.MatchType, mr.Value}).ToList()  
            });
            return Json(returnData);
        }

        [HttpPut]
        [Route("/[controller]/UpdateStyleSetEntryValues/{styleSet}/{id}/{matchOrder}/{entryName}/{isGameElement}")]
        public void UpdateStyleSetEntryValues(string styleSet, long id, int matchOrder, string entryName, bool isGameElement)
        {
            //Hasnt yet been tested.
            var db = new PraxisContext();
            var data = db.TagParserEntries.FirstOrDefault(t => t.Id == id);
            data.MatchOrder = matchOrder;
            data.Name = entryName;
            data.IsGameElement = isGameElement;
            db.SaveChanges();
        }
    }
}
