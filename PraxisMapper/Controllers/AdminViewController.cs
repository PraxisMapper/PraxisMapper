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

        [Route("/[controller]/GetAreaInfo/{sourceElementId}/{sourceElementType}")]
        public ActionResult GetAreaInfo(long sourceElementId, int sourceElementType)
        {
            var db = new PraxisContext();
            var area = db.StoredOsmElements.Include(e => e.Tags).Where(e => e.sourceItemID == sourceElementId && e.sourceItemType == sourceElementType).FirstOrDefault();
            if (area == null)
                return View();

            TagParser.ApplyTags(new System.Collections.Generic.List<DbTables.StoredOsmElement>() { area });
            ViewBag.areaname = area.name;
            ViewBag.type = area.GameElementName;
            ViewBag.isGenerated = area.IsGenerated;
            ViewBag.isUserProvided = area.IsUserProvided;

            var geoarea = Converters.GeometryToGeoArea(area.elementGeometry.Envelope);
            geoarea = new Google.OpenLocationCode.GeoArea(geoarea.SouthLatitude - ConstantValues.resolutionCell10, 
                geoarea.WestLongitude - ConstantValues.resolutionCell10,
                geoarea.NorthLatitude + ConstantValues.resolutionCell10, 
                geoarea.EastLongitude + ConstantValues.resolutionCell10); //add some padding to the edges.
            ImageStats istats = new ImageStats(geoarea, (int)(geoarea.LongitudeWidth / ConstantValues.resolutionCell11Lon), (int)(geoarea.LatitudeHeight / ConstantValues.resolutionCell11Lat));
            //sanity check: we don't want to draw stuff that won't fit in memory, so check for size and cap it if needed
            if (istats.imageSizeX * istats.imageSizeY > 8000000)
            {
                var ratio = geoarea.LongitudeWidth / geoarea.LatitudeHeight;
                istats = new ImageStats(geoarea, (int)(2000 * ratio), (int)(2000 * ratio));
            }
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            var tile = MapTiles.DrawAreaAtSize(istats);
            sw.Stop();

            ViewBag.imageString = "data:image/png;base64," + Convert.ToBase64String(tile);
            ViewBag.timeToDraw = sw.Elapsed;
            var places = CoreComponents.Place.GetPlaces(istats.area);
            ViewBag.placeCount = places.Count();
            var grouped = places.GroupBy(p => p.GameElementName);
            string areasByType = "";
            foreach (var g in grouped)
                areasByType += g.Key + g.Count() + "<br />";

            ViewBag.areasByType = areasByType;

            return View();
        }
    }
}
