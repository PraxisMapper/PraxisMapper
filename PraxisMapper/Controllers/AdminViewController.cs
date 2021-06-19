using CoreComponents;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PraxisMapper.Classes;
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

    }
}
