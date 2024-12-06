using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;
using PraxisCore;
using PraxisCore.Support;
using PraxisMapper.Classes;
using System.Text.Json;

namespace PraxisMapper.Controllers {
    [Route("[controller]")]
    public class SlippyController : Controller, IPraxisPlugin 
    {
        public static string PrivacyPolicy = "";
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            base.OnActionExecuting(context);
            context.CheckCache(Request.Path, "");
        }

        [HttpGet]
        [Route("/[controller]")]
        [Route("/[controller]/Index")]
        public IActionResult Index() {
            try {
                return View("Index");
            }
            catch (Exception ex) {
                return null;
            }
        }

        [HttpGet]
        [Route("/[controller]/Configs")]
        public JsonResult Configs()
        {
            //Overlays have a transparent background, base layers have an opaque background.
            //String of values. 
            var db = new PraxisContext();
            var baseLayers = db.GlobalData.Where(g => g.DataKey.StartsWith("SlippyBase-")).ToList();
            var overlays = db.GlobalData.Where(g => g.DataKey.StartsWith("SlippyOverlay-")).ToList();
            var results = baseLayers.Select(c => new { key = c.DataKey.Replace("SlippyBase-", ""), value = c.DataValue.ToUTF8String(), isOverlay = false }).ToList();
            results.AddRange(overlays.Select(c => new { key = c.DataKey.Replace("SlippyOverlay-", ""), value = c.DataValue.ToUTF8String(), isOverlay = true }).ToList());
            var response = Json(results);
            PraxisCacheHelper.SetCache(Request.Path, response, 900); //900 seconds = 15 minutes.
            return response;
        }
    }
}
