using Google.OpenLocationCode;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PraxisCore;
using System.Linq;
using static PraxisCore.DbTables;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class StyleDataController : Controller
    {
        //some of these will take JSON strings up, parse and reapply them rather than having a ton of parameters
        IConfiguration Configuration;

        public StyleDataController(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            if (Configuration.GetValue<bool>("enableStyleDataEndpoints") == false)
                HttpContext.Abort();
        }


        [HttpGet]
        [Route("/[controller]/GetStyleSetEntryNames/{styleSet}")]
        [Route("/[controller]/Names/{styleSet}")]
        public string GetStyleSetEntryNames(string styleSet)
        {
            var db = new PraxisContext();
            var data = db.StyleEntries.Where(t => t.StyleSet == styleSet).OrderBy(t => t.MatchOrder).Select(t => t.Name).ToList();
            return string.Join('|', data);
        }

        //These 2 functions do need to use IDs in case names change.
        [HttpGet]
        [Route("/[controller]/GetStyleSetEntryValues/{styleSet}/{entryName}")]
        [Route("/[controller]/Entry/{styleSet}/{entryName}")]
        public string GetStyleSetEntryValues(string styleSet, string entryName)
        {
            var db = new PraxisContext();
            var data = db.StyleEntries.FirstOrDefault(t => t.StyleSet == styleSet && t.Name == entryName);
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
            var data = db.StyleEntries.Include("TagParserMatchRules").Include("paintOperations").Where(t => t.StyleSet == styleSet).ToList();
            var returnData = data.Select(x => new { 
                x.Id, 
                x.Name, 
                x.StyleSet, 
                x.IsGameElement, 
                x.MatchOrder, 
                paintOperations = x.PaintOperations.Select(po => new { po.FileName, po.FillOrStroke, po.FromTag, po.HtmlColorCode, po.Id, po.LayerId, po.LinePattern, po.LineWidthDegrees, po.MaxDrawRes, po.MinDrawRes, po.Randomize }).ToList(),
                MatchRules = x.StyleMatchRules.Select(mr => new { mr.Id, mr.Key, mr.MatchType, mr.Value}).ToList()  
            });
            return Json(returnData);
        }

        [HttpPut]
        [Route("/[controller]/UpdateStyleSetEntryValues/{styleSet}/{id}/{matchOrder}/{entryName}/{isGameElement}")]
        [Route("/[controller]/Entry/{styleSet}/{id}/{matchOrder}/{entryName}/{isGameElement}")]
        public void UpdateStyleSetEntryValues(string styleSet, long id, int matchOrder, string entryName, bool isGameElement)
        {
            //Hasnt yet been tested.
            var db = new PraxisContext();
            var data = db.StyleEntries.FirstOrDefault(t => t.Id == id);
            if (data == null)
            {
                data = new DbTables.StyleEntry();
                db.StyleEntries.Add(data);
            }
            data.MatchOrder = matchOrder;
            data.Name = entryName;
            data.IsGameElement = isGameElement;
            db.SaveChanges();
        }

        [HttpPut]
        [Route("/[controller]/UpdateStyleSetEntryValues/{styleSet}/{id}/{matchOrder}/{entryName}/{isGameElement}")]
        [Route("/[controller]/MatchRule/{id}/{matchType}/{key}/{value}")]
        public void UpdateMatchRule(long id, string matchType, string key, string value)
        {
            //Hasnt yet been tested.
            var db = new PraxisContext();
            var tagrule = db.StyleMatchRules.FirstOrDefault(t => t.Id == id);
            if (tagrule == null)
            {
                tagrule = new DbTables.StyleMatchRule();
                db.StyleMatchRules.Add(tagrule);
            }
            tagrule.MatchType = matchType;
            tagrule.Key = key;
            tagrule.Value = value;
            db.SaveChanges();
        }

        [HttpPut]
        public void UpdateStylePaint([FromBody] StylePaint paint)
        {
            //placeholder, requires testing to make sure paint loads correctly from the body.
            var db = new PraxisContext();
            var data = db.StylePaints.First();
            if (data == null)
                data = new StylePaint();
            data.FileName = paint.FileName;
            data.FillOrStroke = paint.FillOrStroke;
            data.FromTag = paint.FromTag;
            data.HtmlColorCode = paint.HtmlColorCode;
            data.LayerId = paint.LayerId;
            data.LinePattern = paint.LinePattern;
            data.LineWidthDegrees = paint.LineWidthDegrees;
            data.MaxDrawRes = paint.MaxDrawRes;
            data.MinDrawRes = paint.MinDrawRes;
            data.Randomize = paint.Randomize;

            db.SaveChanges();
        }

        [HttpPut]
        [Route("/[controller]/Bitmap/{filename}")]
        public void InsertBitmap(string filename)
        {
            //NOTE: this one rejects overwriting existing entries to avoid potential griefing.
            //
            var db = new PraxisContext();
            if(db.StyleBitmaps.Any(d => d.Filename == filename))
                return;

            var endData = GenericData.ReadBody(Request.BodyReader, (int)Request.ContentLength);
            var data = new StyleBitmap();
            data.Data = endData;
            data.Filename = filename;
            db.StyleBitmaps.Add(data);
            db.SaveChanges();
        }

        public void AskForCreatedAreas(string plusCode)
        {
            //This may belong somewhere else, but this should be a server-side function.
            //Check if there are areas in the listed plusCode, and if not make one or two.
            var db = new PraxisContext();
            var area = OpenLocationCode.DecodeValid(plusCode);
            var places = PraxisCore.Place.GetPlaces(area);
            places = places.Where(p => p.Tags.Any(t => t.Key == "generated" && t.Value == "praxisMapper")).ToList();

            if (places.Count == 0)
                return;

            //now generate a place here.
            //PraxisCore.Place.CreateInterestingPlaces(plusCode, true);
        }
    }
}
