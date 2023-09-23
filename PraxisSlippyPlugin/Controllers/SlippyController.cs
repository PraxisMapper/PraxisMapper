using Microsoft.AspNetCore.Mvc;
using PraxisCore;
using PraxisCore.Support;
using System.Text.Json;

namespace PraxisMapper.Controllers {
    [Route("[controller]")]
    public class SlippyController : Controller, IPraxisPlugin {
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
            //TODO: determine how to pick which entries are Overlays and which are the main BG tiles.
            //Overlays have a transparent background, base layers have an opaque background.
            //String of values. 
            var db = new PraxisContext();
            var baseLayers = db.GlobalData.Where(g => g.DataKey.StartsWith("SlippyBase-")).ToList();
            var overlays = db.GlobalData.Where(g => g.DataKey.StartsWith("SlippyOverlay-")).ToList();
            var results = baseLayers.Select(c => new { key = c.DataKey.Replace("SlippyBase-", ""), value = c.DataValue.ToUTF8String(), isOverlay = false }).ToList();
            results.AddRange(overlays.Select(c => new { key = c.DataKey.Replace("SlippyOverlay-", ""), value = c.DataValue.ToUTF8String(), isOverlay = true }).ToList());
            return Json(results);
        }
    }
}
