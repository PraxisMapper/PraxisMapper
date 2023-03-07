using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PraxisCore;
using PraxisCore.Support;

namespace PraxisMunicipalityPlugin.Controllers {
    [ApiController]
    [Route("[controller]")]
    public class MunicipalityController : Controller, IPraxisPlugin {
        public class MuniData {
            public string name { get; set; }
            public string level { get; set; }

            public MuniData(string n, string l) {
                name = n;
                level = l;
            }
        }

        //NOTE: This function may not appear to work correctly for rural players. But, it does!
        //Turns out, a lot of smaller towns/villages/hamlets/2 shacks at an intersection/etc. sized cities don't have well defined boundaries,
        //and on a map are just a point to put the name label. With no way to even guess what the correct size or area is. They resolve up to the township or county covering them.
        //(Terms and meanings may vary with country, but this behavior is likely to be common globally).

        [HttpGet]
        [Route("/[controller]/Municipality/{plusCode}")]
        [Route("/[controller]/Muni/{plusCode}")]
        public string Muni(string plusCode) {
            Response.Headers.Add("X-noPerfTrack", "Muni/VARSREMOVED");
            var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var location = plusCode.ToPolygon();
            var places = db.Places.Include(p => p.Tags).Where(p => location.Intersects(p.ElementGeometry) && p.Tags.Any(pp => pp.Key == "admin_level")).ToList();
            if (places == null || places.Count == 0)
                return "";

            var smallestPlace = places.OrderByDescending(p => p.Tags.FirstOrDefault(t => t.Key == "admin_level").Value.ToInt()).FirstOrDefault();

            return TagParser.GetName(smallestPlace);
        }


        [HttpGet]
        [Route("/[controller]/MunicipalityAll/{plusCode}")]
        [Route("/[controller]/MuniAll/{plusCode}")]
        public List<MuniData> AllMuni(string plusCode) {
            Response.Headers.Add("X-noPerfTrack", "MuniAll/VARSREMOVED");
            var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var location = plusCode.ToPolygon();
            var places = db.Places.Include(p => p.Tags).Where(p => location.Intersects(p.ElementGeometry) && p.Tags.Any(pp => pp.Key == "admin_level")).ToList();
            var allPlaces = places.OrderBy(p => p.Tags.FirstOrDefault(t => t.Key == "admin_level").Value.ToInt())
                .Select(p => new MuniData(TagParser.GetName(p), p.Tags.FirstOrDefault(t => t.Key == "admin_level").Value)).ToList();

            return allPlaces;
        }

        [HttpGet]
        [Route("/[controller]/PlaceName/{plusCode}")]
        [Route("/[controller]/Place/{plusCode}")]
        public string PlaceName(string plusCode) {
            Response.Headers.Add("X-noPerfTrack", "PlaceName/VARSREMOVED");
            var db = new PraxisContext();
            db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            var poly = plusCode.ToPolygon();
            var places = db.Places.Include(s => s.Tags).Where(md => poly.Intersects(md.ElementGeometry)).ToList();
            TagParser.ApplyTags(places, "mapTiles");
            var place = places.Where(p => p.IsGameElement).OrderByDescending(w => w.ElementGeometry.Area).ThenByDescending(w => w.ElementGeometry.Length).LastOrDefault();

            var name = TagParser.GetName(place);
            if (name == "")
                name = TagParser.GetStyleEntry(place).Name;

            return name;
        }
    }
}