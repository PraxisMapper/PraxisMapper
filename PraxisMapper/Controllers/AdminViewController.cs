using CoreComponents;
using CoreComponents.Support;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PraxisMapper.Classes;
using System;
using System.Linq;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    public class AdminViewController : Controller
    {
        [HttpGet]
        [Route("/[controller]")]
        [Route("/[controller]/Index")]
        public IActionResult Index()
        {
            return View();
        }

        public JsonResult GetScavengerHunts()
        {
            PerformanceTracker pt = new PerformanceTracker("GetScavengerHunts");
            var db = new PraxisContext();
            var results = db.scavengerHunts.ToList();
            pt.Stop();
            return Json(results);
        }

        public JsonResult GetScavengerHuntEntries(long scavengerHuntId)
        {
            PerformanceTracker pt = new PerformanceTracker("GetScavengerHunts");
            var db = new PraxisContext();
            var results = db.scavengerHuntEntries.Where(s => s.ScavengerHunt.id == scavengerHuntId).ToList(); //might be faster to .Include() the entries on the hunt itself?
            pt.Stop();
            return Json(results);
        }

        public void ExpireMapTiles()
        {
            PerformanceTracker pt = new PerformanceTracker("ExpireMapTilesAdmin");
            var db = new PraxisContext();
            //TODO: this is a thing where I probably don't want to load each entry into memory, so using raw SQL for performance here instead.
            //Confirm this is faster, and by how much.
            string sql = "UPDATE MapTiles SET ExpireOn = CURRENT_TIMESTAMP"; //TODO: check if PostgreSQL/MariaDB/SQL Server need variants on the command.
            db.Database.ExecuteSqlRaw(sql);
            sql = "UPDATE SlippyMapTiles SET ExpireOn = CURRENT_TIMESTAMP"; //sa
            db.Database.ExecuteSqlRaw(sql);
            pt.Stop();
        }

        [Route("/[controller]/GetMapTileInfo/{zoom}/{x}/{y}")]
        public ActionResult GetMapTileInfo(int x, int y, int zoom) //This should have a view.
        {
            //Draw the map tile, with extra info to send over.
            ImageStats istats = new ImageStats(zoom, x, y, MapTiles.MapTileSizeSquare, MapTiles.MapTileSizeSquare);

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            var tile = MapTiles.DrawAreaAtSize(istats);
            sw.Stop();

            ViewBag.placeCount = CoreComponents.Place.GetPlaces(istats.area).Count();
            ViewBag.timeToDraw = sw.Elapsed.ToString();
            ViewBag.imageString = "data:image/png;base64," + Convert.ToBase64String(tile);

            return View();
        }
    }
}
