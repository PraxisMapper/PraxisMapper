using DatabaseAccess;
using Google.OpenLocationCode;
using GPSExploreServerAPI.Classes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static DatabaseAccess.DbTables;

namespace GPSExploreServerAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class MapDataController : Controller
    {
        private static MemoryCache cache;

        private readonly IConfiguration Configuration;
        public MapDataController(IConfiguration configuration)
        {
            Configuration = configuration;

            if (cache == null && Configuration.GetValue<bool>("enableCaching") == true)
            {
                var options = new MemoryCacheOptions();
                options.SizeLimit = 1024; //1k entries. that's 2.5 4-digit plus code blocks without roads/buildings. If an entry is 300kb on average, this is 300MB of RAM for caching. T3.small has 2GB total, t3.medium has 4GB.
                cache = new MemoryCache(options);
            }
        }
        //Manual map edits:
        //none currently
        //TODO:
        //Call Flex* logic inside of Cell# functions so i have one function doing identical work in all areas (also might mean flex areas need to identify when everything is inside one cell? possibly by size == resolution10/8/6?)
        //Evaluate threading performance with nultiple requests coming in. Many functions seem to have massive performance gains with a single request with parallel loops, not sure if thats true under load.
        //Ponder allowing mapTiles and mapData to be separate databases, with different connection strings?

        [HttpGet]
        [Route("/[controller]/cell6Info/{plusCode6}")]
        [Route("/[controller]/LearnCell6/{plusCode6}")]
        public string LearnCell6(string plusCode6) //The current primary function used by the app. Uses the Cell6 area given to it
        {
            //Send over the plus code to look up.
            PerformanceTracker pt = new PerformanceTracker("LearnCell6");
            var codeString6 = plusCode6;
            string cachedResults = "";
            if (Configuration.GetValue<bool>("enableCaching") && cache.TryGetValue(codeString6, out cachedResults))
            {
                pt.Stop(codeString6);
                return cachedResults;
            }
            var box = OpenLocationCode.DecodeValid(codeString6);

            var places = MapSupport.GetPlaces(OpenLocationCode.DecodeValid(codeString6));  //All the places in this 6-code //NOTE: takes 500ms here, but 6-codes should take ~15ms in perftesting.
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(codeString6);
            //pluscode6 //first 6 digits of this pluscode. each line below is the last 4 that have an area type.
            //pluscode4|name|type  //less data transmitted, an extra string concat per entry phone-side.

            //Notes: 
            //StringBuilder isn't thread-safe, so each thread needs its own, and their results combined later.
            int splitcount = 40; //creates 1600 entries(40x40)
            List<MapData>[] placeArray;
            GeoArea[] areaArray;
            StringBuilder[] sbArray = new StringBuilder[splitcount * splitcount];
            MapSupport.SplitArea(box, splitcount, places, out placeArray, out areaArray);
            System.Threading.Tasks.Parallel.For(0, placeArray.Length, (i) =>
           {
               sbArray[i] = MapSupport.SearchArea(ref areaArray[i], ref placeArray[i]);
           });

            foreach (StringBuilder sbPartial in sbArray)
                sb.Append(sbPartial.ToString());

            string results = sb.ToString();
            var options = new MemoryCacheEntryOptions();
            options.SetSize(1);
            if (Configuration.GetValue<bool>("enableCaching"))
                cache.Set(codeString6, results, options);

            pt.Stop(codeString6);
            return results;
        }

        [HttpGet]
        [Route("/[controller]/surroundingArea/{lat}/{lon}")]
        [Route("/[controller]/LearnSurroundingCell6/{lat}/{lon}")]
        public string LearnSurroundingCell6(double lat, double lon) // pulls in a Cell6 sized area centered on the coords given. Returns full PlusCode name for each Cell10
        {
            //Take in GPS coords
            //Create area the size of a 6-cell plus code centered on that point
            //return the list of 10-cells in that area.
            //Note: caching is disabled on this request. we can't key on lat/lon and expect the cache results to get used. Need to save these results in something more useful.

            PerformanceTracker pt = new PerformanceTracker("LearnSurroundingCell6");
            string pointDesc = lat.ToString() + "|" + lon.ToString();
            //string cachedResults = "";
            //if (cache.TryGetValue(pointDesc, out cachedResults))
            //{
            //    pt.Stop(pointDesc);
            //    return cachedResults;
            //}
            GeoArea box = new GeoArea(new GeoPoint(lat - .025, lon - .025), new GeoPoint(lat + .025, lon + .025));

            var places = MapSupport.GetPlaces(box);  //All the places in this 6-code //NOTE: takes 500ms here, but 6-codes should take ~15ms in perftesting.
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(pointDesc);
            //This endpoint puts the whole 10-digit plus code (without the separator) at the start of the line. I can't guarentee that any digits are shared since this isn't a grid-bound endpoint.

            //Notes: 
            //StringBuilder isn't thread-safe, so each thread needs its own, and their results combined later.
            int splitcount = 40; //creates 1600 entries(40x40)
            List<MapData>[] placeArray;
            GeoArea[] areaArray;
            StringBuilder[] sbArray = new StringBuilder[splitcount * splitcount];
            MapSupport.SplitArea(box, splitcount, places, out placeArray, out areaArray);
            System.Threading.Tasks.Parallel.For(0, placeArray.Length, (i) =>
            {
                sbArray[i] = MapSupport.SearchArea(ref areaArray[i], ref placeArray[i], true);
            });

            foreach (StringBuilder sbPartial in sbArray)
                sb.Append(sbPartial.ToString());

            string results = sb.ToString();
            //var options = new MemoryCacheEntryOptions();
            //options.SetSize(1);
            //cache.Set(pointDesc, results, options);

            pt.Stop(pointDesc);
            return results;
        }

        [HttpGet]
        [Route("/[controller]/adminBounds/{lat}/{lon}")]
        [Route("/[controller]/LearnAdminBounds/{lat}/{lon}")]
        public string LearnAdminBoundaries(double lat, double lon) //Returns only admin boundaries that contain your current point.
        {
            //THe main endpoint excludes admin boundaries
            //this function exclusively gets them.
            PerformanceTracker pt = new PerformanceTracker("LearnAdminBoundaries");
            var box = new GeoArea(new GeoPoint(lat, lon), new GeoPoint(lat + MapSupport.resolution10, lon + MapSupport.resolution10));
            var entriesHere = MapSupport.GetPlaces(box).Where(p => p.type.StartsWith("admin")).OrderBy(p => p.type).ToList();

            StringBuilder sb = new StringBuilder();
            foreach (var entry in entriesHere)
                sb.AppendLine(entry.name + "|" + entry.type);

            pt.Stop();
            return sb.ToString();
        }

        [HttpGet]
        [Route("/[controller]/cell6Info/{lat}/{lon}")]
        [Route("/[controller]/LearnCell6/{lat}/{lon}")]
        public string LearnCell6(double lat, double lon) //convenience method, makes the server do the plusCode grid encode.
        {
            var codeRequested = new OpenLocationCode(lat, lon);
            var sixCell = codeRequested.CodeDigits.Substring(0, 6);
            return LearnCell6(sixCell);
        }

        [HttpGet]
        [Route("/[controller]/TestServerUp")]
        public string TestServerUp()
        {
            //For debug purposes to confirm the webserver is running and reachable.
            return "OK";
        }

        [HttpGet]
        [Route("/[controller]/8cellBitmap/{plusCode8}")]
        [Route("/[controller]/DrawCell8/{plusCode8}")]
        public FileContentResult DrawCell8(string plusCode8)
        {
            PerformanceTracker pt = new PerformanceTracker("DrawCell8");
            //Load terrain data for an 8cell, turn it into a bitmap
            //Will load these bitmaps on the 8cell grid in the game, so you can see what's around you in a bigger area.

            var db = new GpsExploreContext();
            var existingResults = db.MapTiles.Where(mt => mt.PlusCode == plusCode8 && mt.resolutionScale == 10).FirstOrDefault();
            if (existingResults == null || existingResults.MapTileId == null)
            {
                //Create this entry
                //requires a list of colors to use, which might vary per app
                GeoArea eightCell = OpenLocationCode.DecodeValid(plusCode8);
                var places = MapSupport.GetPlaces(eightCell);
                var results = MapSupport.GetAreaMapTile(ref places, eightCell);
                db.MapTiles.Add(new MapTile() { PlusCode = plusCode8, regenerate = false, resolutionScale = 10, tileData = results });
                db.SaveChanges();
                pt.Stop(plusCode8);
                return File(results, "image/png");
            }

            pt.Stop(plusCode8);
            return File(existingResults.tileData, "image/png");
        }

        [HttpGet]
        [Route("/[controller]/8cellBitmap11/{plusCode8}")]
        [Route("/[controller]/DrawCell8Highres/{plusCode8}")]
        public FileContentResult DrawCell8Highres(string plusCode8)
        {
            PerformanceTracker pt = new PerformanceTracker("DrawCell8Highres");
            //Load terrain data for an 8cell, turn it into a bitmap
            //Will load these bitmaps on the 8cell grid in the game, so you can see what's around you in a bigger area.

            var db = new GpsExploreContext();
            var existingResults = db.MapTiles.Where(mt => mt.PlusCode == plusCode8 && mt.resolutionScale == 11).FirstOrDefault();
            if (existingResults == null || existingResults.MapTileId == null)
            {
                //Create this entry
                //requires a list of colors to use, which might vary per app
                GeoArea eightCell = OpenLocationCode.DecodeValid(plusCode8);
                var places = MapSupport.GetPlaces(eightCell);
                var results = MapSupport.GetAreaMapTile11(ref places, eightCell);
                db.MapTiles.Add(new MapTile() { PlusCode = plusCode8, regenerate = false, resolutionScale = 11, tileData = results });
                db.SaveChanges();
                pt.Stop(plusCode8);
                return File(results, "image/png");
            }

            pt.Stop(plusCode8);
            return File(existingResults.tileData, "image/png");
        }

        [HttpGet]
        [Route("/[controller]/10cellBitmap11/{plusCode10}")]
        [Route("/[controller]/DrawCell10Highres/{plusCode10}")]
        public FileContentResult DrawCell10Highres(string plusCode10)
        {
            PerformanceTracker pt = new PerformanceTracker("DrawCell10Highres");
            //Load terrain data for an 8cell, turn it into a bitmap
            //Will load these bitmaps on the 8cell grid in the game, so you can see what's around you in a bigger area.

            var db = new GpsExploreContext();
            var existingResults = db.MapTiles.Where(mt => mt.PlusCode == plusCode10 && mt.resolutionScale == 11).FirstOrDefault();
            if (existingResults == null || existingResults.MapTileId == null)
            {
                //Create this entry
                //requires a list of colors to use, which might vary per app
                //GeoArea TenCell = OpenLocationCode.Decode(plusCode10.Substring(0, 8) + "+" +  plusCode10.Substring(9, 2));
                GeoArea TenCell = OpenLocationCode.DecodeValid(plusCode10);
                var places = MapSupport.GetPlaces(TenCell);
                var results = MapSupport.GetAreaMapTile11(ref places, TenCell);
                db.MapTiles.Add(new MapTile() { PlusCode = plusCode10, regenerate = false, resolutionScale = 11, tileData = results });
                db.SaveChanges();
                pt.Stop(plusCode10);
                return File(results, "image/png");
            }

            pt.Stop(plusCode10);
            return File(existingResults.tileData, "image/png");
        }

        [HttpGet]
        [Route("/[controller]/6cellBitmap/{plusCode6}")]
        [Route("/[controller]/DrawCell6/{plusCode6}")]
        public FileContentResult DrawCell6(string plusCode6)
        {
            //a 11-cell PNG of a 6cell is roughly 140KB, and now takes ~20-40 seconds to generate (20x the data processed, in ~10x the time and 7x the space)
            PerformanceTracker pt = new PerformanceTracker("DrawCell6");
            //Load terrain data for an 6cell, turn it into a bitmap
            //Will load these bitmaps on the 6cell grid in the game, so you can see what's around you in a bigger area?

            var db = new GpsExploreContext();
            var existingResults = db.MapTiles.Where(mt => mt.PlusCode == plusCode6 && mt.resolutionScale == 10).FirstOrDefault();
            if (existingResults == null || existingResults.MapTileId == null)
            {
                //Create this entry
                //requires a list of colors to use, which might vary per app
                GeoArea sixCell = OpenLocationCode.DecodeValid(plusCode6);
                var allPlaces = MapSupport.GetPlaces(sixCell);
                var results = MapSupport.GetAreaMapTile(ref allPlaces, sixCell);
                db.MapTiles.Add(new MapTile() { PlusCode = plusCode6, regenerate = false, resolutionScale = 10, tileData = results });
                db.SaveChanges();
                pt.Stop(plusCode6);
                return File(results, "image/png");
            }

            pt.Stop(plusCode6);
            return File(existingResults.tileData, "image/png");
        }

        [HttpGet]
        [Route("/[controller]/6cellBitmap11/{plusCode6}")]
        [Route("/[controller]/DrawCell6Highres/{plusCode6}")]
        public FileContentResult DrawCell6Highres(string plusCode6)
        {
            //a PNG of a 6cell this way is roughly KB, and now takes  seconds to generate
            PerformanceTracker pt = new PerformanceTracker("DrawCell6Highres");
            //Load terrain data for an 6cell, turn it into a bitmap
            //Will load these bitmaps on the 6cell grid in the game, so you can see what's around you in a bigger area?
            var db = new GpsExploreContext();
            var existingResults = db.MapTiles.Where(mt => mt.PlusCode == plusCode6 && mt.resolutionScale == 11).FirstOrDefault();
            if (existingResults == null || existingResults.MapTileId == null)
            {
                //requires a list of colors to use, which might vary per app. Defined in AreaType
                GeoArea sixCell = OpenLocationCode.DecodeValid(plusCode6);
                var allPlaces = MapSupport.GetPlaces(sixCell);
                var results = MapSupport.GetAreaMapTile11(ref allPlaces, sixCell);
                db.MapTiles.Add(new MapTile() { PlusCode = plusCode6, regenerate = false, resolutionScale = 11, tileData = results });
                db.SaveChanges();
                pt.Stop(plusCode6);
                return File(results, "image/png");
            }
            pt.Stop(plusCode6);
            return File(existingResults.tileData, "image/png");
        }

        [HttpGet]
        [Route("/[controller]/flexBitmap/{lat}/{lon}/{size}/{resolution}")]
        [Route("/[controller]/DrawFlex/{lat}/{lon}/{size}/{resolution}")]
        public FileContentResult DrawFlex(double lat, double lon, double size, int resolution)
        {
            //Flex endpoint doesnt save data to DB or cache stuff yet.
            PerformanceTracker pt = new PerformanceTracker("DrawFlex");
            GeoArea box = new GeoArea(new GeoPoint(lat - (size / 2), lon - (size / 2)), new GeoPoint(lat + (size / 2), lon + (size / 2)));
            var allPlaces = MapSupport.GetPlaces(box);
            byte[] results;
            if (resolution == 10)
                results = MapSupport.GetAreaMapTile(ref allPlaces, box);
            else
                results = MapSupport.GetAreaMapTile11(ref allPlaces, box);
            pt.Stop(lat + "|" + lon + "|" + size + "|" + resolution);
            return File(results, "image/png");
        }


        //public void PrefillDB()
        //{
        //    //An experiment on pre-filling the DB.
        //    //Global data mean this is 25 million 6cells, 
        //    //Estimated to take 216 hours of CPU time on my dev PC. 9 days is impractical for a solo dev on a single PC. Maybe for a company with a cluster that can run lots of stuff.
        //    //Retaining this code as a reminder.
        //    return;


        //    string charpos1 = OpenLocationCode.CodeAlphabet.Substring(0, 9);
        //    string charpos2 = OpenLocationCode.CodeAlphabet.Substring(0, 18);

        //    var db = new GpsExploreContext();
        //    db.ChangeTracker.AutoDetectChangesEnabled = false;
        //    int counter = 0;

        //    foreach (var c1 in charpos1)
        //        foreach (var c2 in charpos2)
        //            foreach (var c3 in OpenLocationCode.CodeAlphabet)
        //                foreach (var c4 in OpenLocationCode.CodeAlphabet)
        //                    foreach (var c5 in OpenLocationCode.CodeAlphabet)
        //                        foreach (var c6 in OpenLocationCode.CodeAlphabet)
        //                        {
        //                            string plusCode = string.Concat(c1, c2, c3, c4, c5, c6);
        //                            var data = Cell6Info(plusCode);
        //                            //db.PremadeResults.Add(new PremadeResults(){ Data = data, PlusCode6 = plusCode });
        //                            counter++;
        //                            if (counter >= 1000)
        //                            {
        //                                db.SaveChanges();
        //                                counter = 0;
        //                            }
        //                        }
        //}

        [HttpGet]
        [Route("/[controller]/GetPoint/{lat}/{lon}")]
        [Route("/[controller]/TestPoint/{lat}/{lon}")]
        public void TestPoint(double lat, double lon)
        {
            //Do a DB query on where you're standing for interesting places.
            //might be more useful for some games that don't need a map.

            //Exact point for area? or 10cell space to find trails too?
            var places = MapSupport.GetPlaces(new OpenLocationCode(lat, lon).Decode());
            var results = MapSupport.FindPlacesIn10Cell(lon, lat, ref places, true);
        }

        [HttpGet]
        [Route("/[controller]/CheckArea/{id}")]
        [Route("/[controller]/TestMapDataEntry/{id}")]
        public string TestMapDataEntry(long id)
        {
            //Another test method exposed here for me.
            return MapSupport.LoadDataOnArea(id);
        }


        //this lets the app decide how much it wants to download without the server writing a new function every time.
        [HttpGet]
        [Route("/[controller]/flexArea/{lat}/{lon}/{size}")]
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

            var places = MapSupport.GetPlaces(box);  //All the places in this area
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(pointDesc);
            //This endpoint puts the whole 10-digit plus code (without the separator) at the start of the line. I can't guarentee that any digits are shared since this isn't a grid-bound endpoint.

            //Notes: 
            //StringBuilders isn't thread-safe, so each thread needs its own, and their results combined later.

            //This is sort of a magic formula I wandered into.
            // Sqrt(Size / resolution10 ) * 2 is my current logic.
            int splitcount = (int)Math.Floor(Math.Sqrt(size / MapSupport.resolution10) * 2);
            List<MapData>[] placeArray;
            GeoArea[] areaArray;
            StringBuilder[] sbArray = new StringBuilder[splitcount * splitcount];
            MapSupport.SplitArea(box, splitcount, places, out placeArray, out areaArray);
            System.Threading.Tasks.Parallel.For(0, placeArray.Length, (i) =>
            {
                sbArray[i] = MapSupport.SearchArea(ref areaArray[i], ref placeArray[i], true);
            });

            foreach (StringBuilder sbPartial in sbArray)
                sb.Append(sbPartial.ToString());

            string results = sb.ToString();
            pt.Stop(pointDesc + "|" + size);
            return results;
        }

        [HttpGet]
        [Route("/[controller]/CalcAreaPoints/{plusCode8}")]
        [Route("/[controller]/CalculateAreaPoints/{plusCode8}")]
        public string CalculateAreaPoints(string plusCode8)
        {
            //If you want to own the part of a MapData entry within a cell8, this calculates how many squares it takes up (and therefore how many points it takes to claim it)
            PerformanceTracker pt = new PerformanceTracker("CalculateAreaPoints");
            GeoArea box = OpenLocationCode.DecodeValid(plusCode8);
            var places = MapSupport.GetPlaces(box);
            var plusCodeCoords = MapSupport.MakeBox(box);
            var plusCodePoly = MapSupport.factory.CreatePolygon(plusCodeCoords);
            var results = MapSupport.GetPointsForArea(plusCodePoly, places);
            pt.Stop();

            return results;
        }

        [HttpGet]
        [Route("/[controller]/CalcFullAreaPoints/{plusCode8}")]
        [Route("/[controller]/CalculateFullAreaPoints/{plusCode8}")]
        public string CalculateFullAreaPoints(string plusCode8)
        {
            //If you want to claim an entire MapData entry, no matter the size, this calculates how many squares it takes up (and therefore how many points it takes to claim it)
            PerformanceTracker pt = new PerformanceTracker("CalculateFullAreaPoints");
            GeoArea box = OpenLocationCode.DecodeValid(plusCode8);
            var places = MapSupport.GetPlaces(box);
            var results =  MapSupport.GetPointsForFullArea(places);
            pt.Stop();
            return results;
        }

        [HttpGet]
        [Route("/[controller]/CalcFlexAreaPoints/{lat}/{lon}/{size}")]
        [Route("/[controller]/CalculateFlexAreaPoints/{lat}/{lon}/{size}")]
        public string CalculateFlexAreaPoints(double lat, double lon, double size)
        {
            PerformanceTracker pt = new PerformanceTracker("CalculateFlexAreaPoints");
            GeoArea box = new GeoArea(new GeoPoint(lat - (size / 2), lon - (size / 2)), new GeoPoint(lat + (size / 2), lon + (size / 2)));
            var places = MapSupport.GetPlaces(box);
            var coords = MapSupport.MakeBox(box);
            var poly = MapSupport.factory.CreatePolygon(coords);
            var results = MapSupport.GetPointsForArea(poly, places);
            pt.Stop();
            return results;
        }

        [HttpGet]
        [Route("/[controller]/CalcFlexFullAreaPoints/{lat}/{lon}/{size}")]
        [Route("/[controller]/CalculateFlexFullAreaPoints/{lat}/{lon}/{size}")]
        public string CalculateFlexFullAreaPoints(double lat, double lon, double size)
        {
            PerformanceTracker pt = new PerformanceTracker("CalculateFlexFullAreaPoints");
            GeoArea box = new GeoArea(new GeoPoint(lat - (size / 2), lon - (size / 2)), new GeoPoint(lat + (size / 2), lon + (size / 2)));
            var places = MapSupport.GetPlaces(box);
            var coords = MapSupport.MakeBox(box);
            var poly = MapSupport.factory.CreatePolygon(coords);
            var results =  MapSupport.GetPointsForArea(poly, places);
            pt.Stop();
            return results;
        }

    }
}
