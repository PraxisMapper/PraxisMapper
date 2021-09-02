using CoreComponents;
using Google.OpenLocationCode;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using PraxisMapper.Classes;
using SkiaSharp;
using System;
using System.Linq;
using System.Text;
using static CoreComponents.AreaTypeInfo;
using static CoreComponents.ConstantValues;
using static CoreComponents.DbTables;
using static CoreComponents.Place;
using static CoreComponents.ScoreData;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class MapDataController : Controller
    {
        //MapDataController handles commands related to reading StoredOsmElement entries in an area. 
        //Drawing tiles, looking up interesting areas for gameplay, etc happen here.
        //CalculateX commands are here because they read the MapData table, but the player doing something with them happens in GameplayController.

        private static MemoryCache cache;
        private readonly IConfiguration Configuration;
        public MapDataController(IConfiguration configuration)
        {
            Configuration = configuration;

            if (cache == null && Configuration.GetValue<bool>("enableCaching"))
            {
                var options = new MemoryCacheOptions();
                options.SizeLimit = 1024; //1k entries. that's 2.5 4-digit plus code blocks without roads/buildings. If an entry is 300kb on average, this is 300MB of RAM for caching. T3.small has 2GB total, t3.medium has 4GB.
                cache = new MemoryCache(options);
            }
        }

        [HttpGet]
        [Route("/[controller]/LearnCell8/{plusCode8}")]
        [Route("/[controller]/LearnCell/{plusCode}")]
        public string LearnCell(string plusCode, int fullCode = 0) //The primary function used by the original intended game mode.
        {
            //TODO: this is pretty hard coded to only work for Cell8s. I will have to work on it and the functions farther down the chain to make this work for any size pluscode.
            //fullcode = 1 is probably a little faster on the device, but 0 currently matches the app and im testing on the cloud server, so 0 this stays for now.
            //Send over the plus code to look up.
            PerformanceTracker pt = new PerformanceTracker("LearnCell");
            var codeString = plusCode;
            string cachedResults = "";
            if (Configuration.GetValue<bool>("enableCaching") && cache.TryGetValue(codeString, out cachedResults))
            {
                pt.Stop(codeString);
                return cachedResults;
            }
            GeoArea box = OpenLocationCode.DecodeValid(codeString);
            var places = GetPlaces(box); //, includeGenerated: Configuration.GetValue<bool>("generateAreas")  //All the places in this 8-code

            //TODO: restore the auto-generate interesting areas logic with v4. This will mean making areas if there's 0 IsGameElement values in the results, since it's all in 1 table now.
            //if (Configuration.GetValue<bool>("generateAreas") && !places.Any(p => p.AreaTypeId < 13 || p.AreaTypeId == 100)) //check for 100 to not make new entries in the same spot.
            //{
            //    var newAreas = CreateInterestingPlaces(codeString8);
            //    places = newAreas.Select(g => new MapData() { MapDataId = g.GeneratedMapDataId + 100000000, place = g.place, type = g.type, name = g.name, AreaTypeId = g.AreaTypeId }).ToList();
            //}

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(codeString);
            //pluscode8 //first 6 digits of this pluscode. each line below is the last 4 that have an area type.
            //pluscode2|name|type|MapDataID  //less data transmitted, an extra string concat per entry phone-side.

            var data = SearchArea(ref box, ref places, (fullCode == 1));
            var results = String.Join("/r/n", data.Select(d => d.Key + "|" + d.Value.Select(v => v.Name + "|" + v.areaType + "|" + v.OsmElementId + v.OsmElementType)));

            pt.Stop(codeString);
            return codeString + Environment.NewLine + results;
        }

        [HttpGet]
        [Route("/[controller]/LearnAdminBounds/{lat}/{lon}")]
        public string LearnAdminBoundaries(double lat, double lon) //Returns only admin boundaries that contain your current point.
        {
            //THe main endpoint excludes admin boundaries
            //this function exclusively gets them.
            PerformanceTracker pt = new PerformanceTracker("LearnAdminBoundaries");
            var box = new GeoArea(new GeoPoint(lat, lon), new GeoPoint(lat + resolutionCell10, lon + resolutionCell10));
            var entriesHere = GetPlaces(box).Where(p => p.GameElementName == "admin").OrderByDescending(p => p.AreaSize).ToList();

            StringBuilder sb = new StringBuilder();
            foreach (var entry in entriesHere)
                sb.AppendLine(entry.name + "|" + entry.GameElementName + "|" + entry.sourceItemID);

            pt.Stop();
            return sb.ToString();
        }

        [HttpGet]
        [Route("/[controller]/LearnCell8/{lat}/{lon}")]
        public string LearnCell8(double lat, double lon) //convenience method, makes the server do the plusCode grid encode.
        {
            var codeRequested = new OpenLocationCode(lat, lon);
            var Cell8 = codeRequested.CodeDigits.Substring(0, 8);
            return LearnCell(Cell8, 1);
        }

        [HttpGet]
        [Route("/[controller]/DrawCell8Highres/{plusCode8}/{styleSet}")]
        [Route("/[controller]/DrawCell8Highres/{plusCode8}")]
        [Route("/[controller]/DrawCell/{plusCode8}/{styleSet}")]
        [Route("/[controller]/DrawCell/{plusCode8}")]
        public FileContentResult DrawCell(string plusCode8, string styleSet = "mapTiles")
        {
            //NOTE: this will work for all PlusCode sized passed in. Default to anticipating 8s as the default.
            //TODO This function might get replaced with GetPlusCode in MapTileController.Its effectively the smae thing.
            PerformanceTracker pt = new PerformanceTracker("DrawCell");
            //Load terrain data for an 8cell, turn it into a bitmap
            var db = new PraxisContext();
            var existingResults = db.MapTiles.Where(mt => mt.PlusCode == plusCode8 && mt.resolutionScale == 11 && mt.styleSet == styleSet).FirstOrDefault();
            if (existingResults == null || existingResults.MapTileId == null)
            {
                //Create this entry
                //requires a list of colors to use, which might vary per app
                GeoArea eightCell = OpenLocationCode.DecodeValid(plusCode8);
                //var places = GetPlaces(eightCell); //, includeGenerated: Configuration.GetValue<bool>("generateAreas")
                var results = MapTiles.DrawPlusCode(plusCode8, styleSet, true);
                db.MapTiles.Add(new MapTile() { PlusCode = plusCode8, CreatedOn = DateTime.Now, styleSet = styleSet, resolutionScale = 11, tileData = results, areaCovered = Converters.GeoAreaToPolygon(eightCell) });
                db.SaveChanges();
                pt.Stop(plusCode8);
                return File(results, "image/png");
            }

            pt.Stop(plusCode8);
            return File(existingResults.tileData, "image/png");
        }



        [HttpGet]
        [Route("/[controller]/GetPoint/{lat}/{lon}")]
        [Route("/[controller]/TestPoint/{lat}/{lon}")]
        public void TestPoint(double lat, double lon)
        {
            //Do a DB query on where you're standing for interesting places.
            //might be more useful for some games that don't need a map.
            //attach a debug point here and read the results.

            //Exact point for area? or 10cell space to find trails too?
            var places = GetPlaces(new OpenLocationCode(lat, lon).Decode()); // , includeGenerated: Configuration.GetValue<bool>("generateAreas")
            var results = FindPlacesInCell10(lon, lat, ref places, true);
        }

        [HttpGet]
        [Route("/[controller]/CheckArea/{id}")]
        [Route("/[controller]/TestMapDataEntry/{id}")]
        public string TestMapDataEntry(long id)
        {
            //Another test method exposed here for me.
            return LoadDataOnPlace(id);
        }

        //this lets the app decide how much it wants to download without the server writing a new function every time.
        [HttpGet]
        [Route("/[controller]/LearnSurroundingFlex/{lat}/{lon}/{size}")]
        public string LearnSurroundingFlex(double lat, double lon, double size)
        {
            //Take in GPS coords
            //Create area the size size degrees centered on that point
            //return the list of 10-cells in that area.
            //Note: caching is disabled on this request. we can't key on lat/lon and expect the cache results to get used. Need to save these results in something more useful.

            PerformanceTracker pt = new PerformanceTracker("LearnSurroundingFlex");
            string pointDesc = lat.ToString() + "|" + lon.ToString();
            //Caching for this will be keyed to the first 2? decimal places of each value, if .01 is the area, get half of that on each side (ex, at 40.00, load 39.995-40.005) 
            //so the third digit is the lowest one that would change.
            //This area is 
            //string cachedResults = "";
            //if (cache.TryGetValue(pointDesc, out cachedResults))
            //{
            //    pt.Stop(pointDesc);
            //    return cachedResults;
            //}
            GeoArea box = new GeoArea(new GeoPoint(lat - (size / 2), lon - (size / 2)), new GeoPoint(lat + (size / 2), lon + (size / 2)));

            var places = GetPlaces(box); //includeGenerated: Configuration.GetValue<bool>("generateAreas")  //All the places in this area
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(pointDesc);
            //This endpoint puts the whole 10-digit plus code (without the separator) at the start of the line. I can't guarentee that any digits are shared since this isn't a grid-bound endpoint.

            var data = SearchArea(ref box, ref places, true);
            var results = String.Join("/r/n", data.Select(d => d.Key + "|" + d.Value.Select(v => v.Name + "|" + v.areaType + "|" + v.OsmElementId + v.OsmElementType)));
            pt.Stop(pointDesc + "|" + size);
            return results;
        }

        [HttpGet]
        [Route("/[controller]/CalculateAreaPoints/{plusCode8}")]
        public string CalculateAreaPoints(string plusCode8)
        {
            //If you want to own the part of a MapData entry within a cell8, this calculates how many squares it takes up (and therefore how many points it takes to claim it)
            PerformanceTracker pt = new PerformanceTracker("CalculateAreaPoints");
            GeoArea box = OpenLocationCode.DecodeValid(plusCode8);
            var places = GetPlaces(box); //, includeGenerated: Configuration.GetValue<bool>("generateAreas")
            var plusCodePoly = Converters.GeoAreaToPolygon(box);
            var results = GetScoresForArea(plusCodePoly, places);
            pt.Stop();

            return results;
        }

        [HttpGet]
        [Route("/[controller]/CalculateFullAreaPoints/{plusCode8}")]
        public string CalculateFullAreaPoints(string plusCode8)
        {
            //If you want to claim an entire MapData entry, no matter the size, this calculates how many squares it takes up (and therefore how many points it takes to claim it)
            PerformanceTracker pt = new PerformanceTracker("CalculateFullAreaPoints");
            GeoArea box = OpenLocationCode.DecodeValid(plusCode8);
            var places = GetPlaces(box);// , includeGenerated: Configuration.GetValue<bool>("generateAreas")
            var results =  GetScoresForFullArea(places);
            pt.Stop();
            return results;
        }

        [HttpGet]
        [Route("/[controller]/CalculateFlexAreaPoints/{lat}/{lon}/{size}")]
        public string CalculateFlexAreaPoints(double lat, double lon, double size)
        {
            PerformanceTracker pt = new PerformanceTracker("CalculateFlexAreaPoints");
            GeoArea box = new GeoArea(new GeoPoint(lat - (size / 2), lon - (size / 2)), new GeoPoint(lat + (size / 2), lon + (size / 2)));
            var places = GetPlaces(box); //, includeGenerated: Configuration.GetValue<bool>("generateAreas")
            var poly = Converters.GeoAreaToPolygon(box);
            var results = GetScoresForArea(poly, places);
            pt.Stop();
            return results;
        }

        [HttpGet]
        [Route("/[controller]/CalculateFlexFullAreaPoints/{lat}/{lon}/{size}")]
        public string CalculateFlexFullAreaPoints(double lat, double lon, double size)
        {
            PerformanceTracker pt = new PerformanceTracker("CalculateFlexFullAreaPoints");
            GeoArea box = new GeoArea(new GeoPoint(lat - (size / 2), lon - (size / 2)), new GeoPoint(lat + (size / 2), lon + (size / 2)));
            var places = GetPlaces(box); //, includeGenerated: Configuration.GetValue<bool>("generateAreas")
            var poly = Converters.GeoAreaToPolygon(box);
            var results =  GetScoresForArea(poly, places);
            pt.Stop();
            return results;
        }

        [HttpGet]
        [Route("/[controller]/CalculateMapDataScore/{SourceItemId}/SourceTypeId")]
        public string CalculateMapDataScore(long SourceItemId, int SourceTypeId)
        {
            PerformanceTracker pt = new PerformanceTracker("CalculateMapDataScore");
            var db = new PraxisContext();
            var places = db.StoredOsmElements.Where(m => m.sourceItemID == SourceItemId && m.sourceItemType == SourceTypeId).ToList(); //odds are good that a big enough server would have overlapping IDs for relations and ways. Must check type.
            var results = GetScoresForFullArea(places);
            pt.Stop(SourceTypeId.ToString());
            return results;
        }
    }
}
