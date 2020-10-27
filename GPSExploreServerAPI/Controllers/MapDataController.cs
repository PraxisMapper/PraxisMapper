using DatabaseAccess;
using Google.OpenLocationCode;
using GPSExploreServerAPI.Classes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using NetTopologySuite.Geometries;
using Newtonsoft.Json;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
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
                options.SizeLimit = 1024; //1k entries. that's 2.5 4-digit plus code blocks. If an entry is 300kb on average, this is 300MB of RAM for caching. T3.small has 2GB total, t3.medium has 4GB.
                cache = new MemoryCache(options);
            }
        }
        //Manual map edits:
        //none
        //TODO:
        //No file-wide todos
        //Evaluate threading performance with nultiple requests coming in. Many functions seem to have massive performance gains with a single request with parallel loops, not sure if thats true under load.

        //Cell8Data function removed, significantly out of date.
        //remaking it would mean slightly changes to a copy of Cell6Info. Perhaps just use FlexArea with size .0025 instead

        [HttpGet]
        [Route("/[controller]/cell6Info/{plusCode6}")]
        public string Cell6Info(string plusCode6) //The current primary function used by the app.
        {
            //Send over the plus code to look up.
            PerformanceTracker pt = new PerformanceTracker("Cell6info");
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
        public string GetSurroundingCell6Area(double lat, double lon)
        {
            //Take in GPS coords
            //Create area the size of a 6-cell plus code centered on that point
            //return the list of 10-cells in that area.
            //Note: caching is disabled on this request. we can't key on lat/lon and expect the cache results to get used. Need to save these results in something more useful.

            PerformanceTracker pt = new PerformanceTracker("GetSurrounding6CellArea");
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
        public string GetAdminBoundaries(double lat, double lon)
        {
            //THe main endpoint excludes admin boundaries
            //this function exclusively gets them.
            var box = new GeoArea(new GeoPoint(lat, lon), new GeoPoint(lat + MapSupport.resolution10, lon + MapSupport.resolution10));
            var entriesHere = MapSupport.GetPlaces(box).Where(p => p.type.StartsWith("admin")).OrderBy(p => p.type).ToList();

            StringBuilder sb = new StringBuilder();
            foreach (var entry in entriesHere)
                sb.AppendLine(entry.name + "|" + entry.type);

            return sb.ToString();
        }

        [HttpGet]
        [Route("/[controller]/cell6Info/{lat}/{lon}")]
        public string Cell6Info(double lat, double lon)
        {
            var codeRequested = new OpenLocationCode(lat, lon);
            var sixCell = codeRequested.CodeDigits.Substring(0, 6);
            return Cell6Info(sixCell);
        }

        [HttpGet]
        [Route("/[controller]/test")]
        public string TestDummyEndpoint()
        {
            //For debug purposes to confirm the server is running and reachable.
            return "OK";
        }

        [HttpGet]
        [Route("/[controller]/8cellBitmap/{plusCode8}")]
        public FileContentResult Get8CellBitmap(string plusCode8)
        {
            PerformanceTracker pt = new PerformanceTracker("8CellBitmap");
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
        public FileContentResult Get8CellBitmap11(string plusCode8)
        {
            PerformanceTracker pt = new PerformanceTracker("8CellBitmap11");
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
        [Route("/[controller]/6cellBitmap/{plusCode6}")]
        public FileContentResult Get6CellBitmap(string plusCode6)
        {
            //a 11-cell PNG of a 6cell is roughly 140KB, and now takes ~20-40 seconds to generate (20x the data processed, in ~10x the time and 7x the space)
            PerformanceTracker pt = new PerformanceTracker("6CellBitmap");
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
        public FileContentResult Get6CellBitmap11(string plusCode6)
        {
            //a PNG of a 6cell this way is roughly KB, and now takes  seconds to generate
            PerformanceTracker pt = new PerformanceTracker("6CellBitmap11");
            //Load terrain data for an 6cell, turn it into a bitmap
            //Will load these bitmaps on the 6cell grid in the game, so you can see what's around you in a bigger area?
            //server will create and load these. Should cache them since generating them is time consuming. TODO: save tiles to db.
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
        public FileContentResult GetFlexBitmap(double lat, double lon, double size, int resolution)
        {
            //Flex endpoint doesnt save data to DB or cache stuff yet.
            PerformanceTracker pt = new PerformanceTracker("flexBitmap");
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
        public void GetStuffAtPoint(double lat, double lon)
        {
            //Do a DB query on where you're standing for interesting places.
            //might be more useful for some games that don't need a map.

            //Exact point for area? or 10cell space to find trails too?
            var places = MapSupport.GetPlaces(new OpenLocationCode(lat, lon).Decode());
            var results = MapSupport.FindPlacesIn10Cell(lon, lat, ref places, true);
        }

        [HttpGet]
        [Route("/[controller]/CheckArea/{id}")]
        public string CheckOnArea(long id)
        {
            //Another test method exposed here for me.
            return MapSupport.LoadDataOnArea(id);
        }


        //this lets the app decide how much it wants to download without the server writing a new function every time.
        [HttpGet]
        [Route("/[controller]/flexArea/{lat}/{lon}/{size}")]
        public string GetSurroundingFlexArea(double lat, double lon, double size)
        {
            //Take in GPS coords
            //Create area the size size degrees centered on that point
            //return the list of 10-cells in that area.
            //Note: caching is disabled on this request. we can't key on lat/lon and expect the cache results to get used. Need to save these results in something more useful.

            PerformanceTracker pt = new PerformanceTracker("GetSurroundingFlexArea");
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
        public string CalculateAreasAndPoints(string plusCode8)
        {
            //We are looking to see how to score an area by size in a given area.
            //I think we're looking at 8-codes
            //string PlusCode = "86FRXXWP";
            GeoArea box = OpenLocationCode.DecodeValid(plusCode8);
            var places = MapSupport.GetPlaces(box);
            var plusCodeCoords = MapSupport.MakeBox(box);
            var plusCodePoly = MapSupport.factory.CreatePolygon(plusCodeCoords);

            return MapSupport.GetPointsForArea(plusCodePoly, places);
        }

        [HttpGet]
        [Route("/[controller]/CalcFlexAreaPoints/{lat}/{lon}/{size}")]
        public string CalculateFlexAreasAndPoints(double lat, double lon, double size)
        {
            GeoArea box = new GeoArea(new GeoPoint(lat - (size / 2), lon - (size / 2)), new GeoPoint(lat + (size / 2), lon + (size / 2)));
            var places = MapSupport.GetPlaces(box);
            var coords = MapSupport.MakeBox(box);
            var poly = MapSupport.factory.CreatePolygon(coords);

            return MapSupport.GetPointsForArea(poly, places);
        }

    }
}
