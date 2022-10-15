using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Client;
using PraxisCore;
using PraxisCore.Support;
using SkiaSharp;

namespace PraxisMunicipalityPlugin.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class MunicipalityController : Controller, IPraxisPlugin
    {
        public class MuniData
        {
            public string name { get; set; }
            public string level { get; set; }

            public MuniData(string n, string l)
            { 
                name = n;
                level = l;
            }
        }

        [HttpGet]
        [Route("/[controller]/Municipality/{plusCode}")]
        [Route("/[controller]/Muni/{plusCode}")]
        public string Muni(string plusCode)
        {
            Response.Headers.Add("X-noPerfTrack", "Muni/VARSREMOVED");
            var db = new PraxisContext();
            var location = plusCode.ToPolygon();
            var places = db.Places.Include(p => p.Tags).Where(p => location.Intersects(p.ElementGeometry) && p.Tags.Any(pp => pp.Key == "admin_level")).ToList();
            var smallestPlace = places.OrderByDescending(p => p.Tags.FirstOrDefault(t => t.Key == "admin_level").Value.ToInt()).FirstOrDefault();

            return TagParser.GetPlaceName(smallestPlace.Tags);
        }


        [HttpGet]
        [Route("/[controller]/MunicipalityAll/{plusCode}")]
        [Route("/[controller]/MuniAll/{plusCode}")]
        public List<MuniData> AllMuni(string plusCode)
        {
            Response.Headers.Add("X-noPerfTrack", "MuniAll/VARSREMOVED");
            var db = new PraxisContext();
            var location = plusCode.ToPolygon();
            var places = db.Places.Include(p => p.Tags).Where(p => location.Intersects(p.ElementGeometry) && p.Tags.Any(pp => pp.Key == "admin_level")).ToList();
            var allPlaces = places.OrderBy(p => p.Tags.FirstOrDefault(t => t.Key == "admin_level").Value.ToInt())
                .Select(p => new MuniData(TagParser.GetPlaceName(p.Tags), p.Tags.FirstOrDefault(t => t.Key == "admin_level").Value)).ToList();

            return allPlaces;
        }

        [HttpGet]
        [Route("/[controller]/PlaceName/{plusCode}")]
        [Route("/[controller]/Place/{plusCode}")]
        public string PlaceName(string plusCode)
        {
            Response.Headers.Add("X-noPerfTrack", "PlaceName/VARSREMOVED");
            var db = new PraxisContext();
            var poly = plusCode.ToPolygon();
            var places = db.Places.Include(s => s.Tags).Where(md => poly.Intersects(md.ElementGeometry)).ToList();
            TagParser.ApplyTags(places, "mapTiles");
            var place = places.Where(p => p.IsGameElement).OrderByDescending(w => w.ElementGeometry.Area).ThenByDescending(w => w.ElementGeometry.Length).LastOrDefault();

            var name = TagParser.GetPlaceName(place.Tags);
            if (name == "")
                name = TagParser.GetAreaType(place.Tags);

            return name;
        }
    }
}