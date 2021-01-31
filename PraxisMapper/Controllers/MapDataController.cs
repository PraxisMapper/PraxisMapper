using CoreComponents;
using Google.OpenLocationCode;
using PraxisMapper.Classes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using static CoreComponents.DbTables;
using static CoreComponents.ConstantValues;
using static CoreComponents.Singletons;
using static CoreComponents.Place;
using static CoreComponents.ScoreData;
using static CoreComponents.AreaTypeInfo;

namespace PraxisMapper.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class MapDataController : Controller
    {
        //MapDataController handles commands related to reading MapData entries in an area. 
        //Drawing tiles, looking up interesting areas for gameplay, etc happen here.
        //CalculateX commands are here because they read the MapData table, but the player doing something with them happens in GameplayController.

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

            var places = GetPlaces(OpenLocationCode.DecodeValid(codeString6));  //All the places in this 6-code
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(codeString6);
            //pluscode6 //first 6 digits of this pluscode. each line below is the last 4 that have an area type.
            //pluscode4|name|type|MapDataID  //less data transmitted, an extra string concat per entry phone-side.

            //Notes: 
            //StringBuilder isn't thread-safe, so each thread needs its own, and their results combined later.
            int splitcount = 40; //creates 1600 entries(40x40)
            List<MapData>[] placeArray;
            GeoArea[] areaArray;
            StringBuilder[] sbArray = new StringBuilder[splitcount * splitcount];
            Converters.SplitArea(box, splitcount, places, out placeArray, out areaArray);
            System.Threading.Tasks.Parallel.For(0, placeArray.Length, (i) =>
           {
               sbArray[i] = SearchArea(ref areaArray[i], ref placeArray[i]);
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
        [Route("/[controller]/LearnCell8/{plusCode8}")]
        public string LearnCell8(string plusCode8, int fullCode = 0) //The primary function used by the original intended game mode.
        {
            //This is a web request, so we should remove parallel calls.
            //fullcode = 1 is probably a little faster on the device, but 0 currently matches the app and im testing on the cloud server, so 0 this stays for now.
            //Send over the plus code to look up.
            PerformanceTracker pt = new PerformanceTracker("LearnCell8");
            var codeString8 = plusCode8;
            string cachedResults = "";
            if (Configuration.GetValue<bool>("enableCaching") && cache.TryGetValue(codeString8, out cachedResults))
            {
                pt.Stop(codeString8);
                return cachedResults;
            }
            var box = OpenLocationCode.DecodeValid(codeString8);

            var places = GetPlaces(OpenLocationCode.DecodeValid(codeString8));  //All the places in this 8-code
            if (Configuration.GetValue<bool>("generateAreas") && !places.Any(p => p.AreaTypeId < 13 || p.AreaTypeId == 100)) //check for 100 to not make new entries in the same spot.
            {
                var newAreas = CreateInterestingPlaces(codeString8);
                places = newAreas.Select(g => new MapData() { MapDataId = g.GeneratedMapDataId + 100000000, place = g.place, type = g.type, name = g.name, AreaTypeId = g.AreaTypeId }).ToList();
            }

            //TODO: test this logic for performance without the splitArea code now that I crop all the elements down to just the requested area.
            var cropArea = Converters.GeoAreaToPolygon(box);
            foreach (var p in places)
                p.place = p.place.Intersection(cropArea);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine(codeString8);
            //pluscode6 //first 6 digits of this pluscode. each line below is the last 4 that have an area type.
            //pluscode4|name|type|MapDataID  //less data transmitted, an extra string concat per entry phone-side.

            //Notes: 
            //StringBuilder isn't thread-safe, so each thread needs its own, and their results combined later.
            //int splitcount = 40; //creates 1600 entries(40x40)
            //List<MapData>[] placeArray;
            //GeoArea[] areaArray;
            //StringBuilder[] sbArray = new StringBuilder[splitcount * splitcount];
            //Converters.SplitArea(box, splitcount, places, out placeArray, out areaArray); //This can take a few seconds on some areas. Why? Extremely dense?
            //Converters.SplitArea(box, 1, places, out placeArray, out areaArray); //This can take a few seconds on some areas. Why? Extremely dense?
            //System.Threading.Tasks.Parallel.For(0, placeArray.Length, (i) => //avoid parallel calls on web code. 
            //for (int i = 0; i < placeArray.Length; i++)
            //{
            //sbArray[i] = SearchArea(ref areaArray[i], ref placeArray[i], (fullCode == 1));
            //}

            //foreach (StringBuilder sbPartial in sbArray)
            //sb.Append(sbPartial.ToString());

            GeoArea search = new GeoArea(box);

            //New block to test perf, now that i crop areas correctly.
            sb.Append(SearchArea(ref search, ref places, (fullCode == 1)));

            string results = sb.ToString();
            var options = new MemoryCacheEntryOptions();
            options.SetSize(1);
            if (Configuration.GetValue<bool>("enableCaching"))
                cache.Set(codeString8, results, options);

            pt.Stop(codeString8);
            return results;
        }

        [HttpGet]
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

            var places = GetPlaces(box);  //All the places in this 6-code //NOTE: takes 500ms here, but 6-codes should take ~15ms in perftesting.
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(pointDesc);
            //This endpoint puts the whole 10-digit plus code (without the separator) at the start of the line. I can't guarentee that any digits are shared since this isn't a grid-bound endpoint.

            //Notes: 
            //StringBuilder isn't thread-safe, so each thread needs its own, and their results combined later.
            int splitcount = 40; //creates 1600 entries(40x40)
            List<MapData>[] placeArray;
            GeoArea[] areaArray;
            StringBuilder[] sbArray = new StringBuilder[splitcount * splitcount];
            Converters.SplitArea(box, splitcount, places, out placeArray, out areaArray);
            System.Threading.Tasks.Parallel.For(0, placeArray.Length, (i) =>
            {
                sbArray[i] = SearchArea(ref areaArray[i], ref placeArray[i], true);
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
        [Route("/[controller]/LearnAdminBounds/{lat}/{lon}")]
        public string LearnAdminBoundaries(double lat, double lon) //Returns only admin boundaries that contain your current point.
        {
            //THe main endpoint excludes admin boundaries
            //this function exclusively gets them.
            PerformanceTracker pt = new PerformanceTracker("LearnAdminBoundaries");
            var box = new GeoArea(new GeoPoint(lat, lon), new GeoPoint(lat + resolutionCell10, lon + resolutionCell10));
            var entriesHere = GetPlaces(box).Where(p => p.type.StartsWith("admin")).OrderBy(p => p.type).ToList();

            StringBuilder sb = new StringBuilder();
            foreach (var entry in entriesHere)
                sb.AppendLine(entry.name + "|" + entry.type + "|" + entry.MapDataId);

            pt.Stop();
            return sb.ToString();
        }

        [HttpGet]
        [Route("/[controller]/LearnCell6/{lat}/{lon}")]
        public string LearnCell6(double lat, double lon) //convenience method, makes the server do the plusCode grid encode.
        {
            var codeRequested = new OpenLocationCode(lat, lon);
            var sixCell = codeRequested.CodeDigits.Substring(0, 6);
            return LearnCell6(sixCell);
        }

        [HttpGet]
        [Route("/[controller]/LearnCell8/{lat}/{lon}")]
        public string LearnCell8(double lat, double lon) //convenience method, makes the server do the plusCode grid encode.
        {
            var codeRequested = new OpenLocationCode(lat, lon);
            var Cell8 = codeRequested.CodeDigits.Substring(0, 8);
            return LearnCell8(Cell8, 1);
        }

        [HttpGet]
        [Route("/[controller]/TestServerUp")]
        public string TestServerUp()
        {
            //For debug purposes to confirm the webserver is running and reachable.
            return "OK";
        }

        [HttpGet]
        [Route("/[controller]/DrawCell8/{plusCode8}")]
        public FileContentResult DrawCell8(string plusCode8)
        {
            PerformanceTracker pt = new PerformanceTracker("DrawCell8");
            //Load terrain data for an 8cell, turn it into a bitmap
            //Will load these bitmaps on the 8cell grid in the game, so you can see what's around you in a bigger area.

            var db = new PraxisContext();
            var existingResults = db.MapTiles.Where(mt => mt.PlusCode == plusCode8 && mt.resolutionScale == 10 && mt.mode == 1).FirstOrDefault();
            if (existingResults == null || existingResults.MapTileId == null)
            {
                //Create this entry
                //requires a list of colors to use, which might vary per app
                GeoArea eightCell = OpenLocationCode.DecodeValid(plusCode8);
                var places = GetPlaces(eightCell);
                var results = MapTiles.DrawAreaMapTile(ref places, eightCell, 10);
                db.MapTiles.Add(new MapTile() { PlusCode = plusCode8, CreatedOn = DateTime.Now, mode =1, resolutionScale = 10, tileData = results });
                db.SaveChanges();
                pt.Stop(plusCode8);
                return File(results, "image/png");
            }

            pt.Stop(plusCode8);
            return File(existingResults.tileData, "image/png");
        }

        [HttpGet]
        [Route("/[controller]/DrawCell8Highres/{plusCode8}")]
        public FileContentResult DrawCell8Highres(string plusCode8)
        {
            PerformanceTracker pt = new PerformanceTracker("DrawCell8Highres");
            //Load terrain data for an 8cell, turn it into a bitmap
            //Takes ~5 seconds to draw a busy Cell8, ~5ms to load an existing one.

            var db = new PraxisContext();
            var existingResults = db.MapTiles.Where(mt => mt.PlusCode == plusCode8 && mt.resolutionScale == 11 && mt.mode == 1).FirstOrDefault();
            if (existingResults == null || existingResults.MapTileId == null)
            {
                //Create this entry
                //requires a list of colors to use, which might vary per app
                GeoArea eightCell = OpenLocationCode.DecodeValid(plusCode8);
                var places = GetPlaces(eightCell);
                var results = MapTiles.DrawAreaMapTile(ref places, eightCell, 11);
                db.MapTiles.Add(new MapTile() { PlusCode = plusCode8, CreatedOn = DateTime.Now, mode = 1, resolutionScale = 11, tileData = results });
                db.SaveChanges();
                pt.Stop(plusCode8);
                return File(results, "image/png");
            }

            pt.Stop(plusCode8);
            return File(existingResults.tileData, "image/png");
        }

        [HttpGet]
        [Route("/[controller]/DrawCell10Highres/{plusCode10}")]
        public FileContentResult DrawCell10Highres(string plusCode10)
        {
            PerformanceTracker pt = new PerformanceTracker("DrawCell10Highres");
            //Load terrain data for an 8cell, turn it into a bitmap
            //Will load these bitmaps on the 8cell grid in the game, so you can see what's around you in a bigger area.

            var db = new PraxisContext();
            var existingResults = db.MapTiles.Where(mt => mt.PlusCode == plusCode10 && mt.resolutionScale == 11 && mt.mode == 1).FirstOrDefault();
            if (existingResults == null || existingResults.MapTileId == null)
            {
                //Create this entry
                //requires a list of colors to use, which might vary per app
                //GeoArea TenCell = OpenLocationCode.Decode(plusCode10.Substring(0, 8) + "+" +  plusCode10.Substring(9, 2));
                GeoArea TenCell = OpenLocationCode.DecodeValid(plusCode10);
                var places = GetPlaces(TenCell);
                var results = MapTiles.DrawAreaMapTile(ref places, TenCell, 11);
                db.MapTiles.Add(new MapTile() { PlusCode = plusCode10, CreatedOn = DateTime.Now, mode = 1, resolutionScale = 11, tileData = results });
                db.SaveChanges();
                pt.Stop(plusCode10);
                return File(results, "image/png");
            }

            pt.Stop(plusCode10);
            return File(existingResults.tileData, "image/png");
        }

        [HttpGet]
        [Route("/[controller]/DrawCell6/{plusCode6}")]
        public FileContentResult DrawCell6(string plusCode6)
        {
            //a 10-cell PNG of a 6cell is roughly 140KB, and now takes ~20-40 seconds to generate (20x the data processed, in ~10x the time and 7x the space)
            PerformanceTracker pt = new PerformanceTracker("DrawCell6");
            //Load terrain data for an 6cell, turn it into a bitmap
            //Will load these bitmaps on the 6cell grid in the game, so you can see what's around you in a bigger area?

            var db = new PraxisContext();
            var existingResults = db.MapTiles.Where(mt => mt.PlusCode == plusCode6 && mt.resolutionScale == 10 && mt.mode == 1).FirstOrDefault();
            if (existingResults == null || existingResults.MapTileId == null)
            {
                //Create this entry
                //requires a list of colors to use, which might vary per app
                GeoArea sixCell = OpenLocationCode.DecodeValid(plusCode6);
                var allPlaces = GetPlaces(sixCell);
                var results = MapTiles.DrawAreaMapTile(ref allPlaces, sixCell, 10);
                db.MapTiles.Add(new MapTile() { PlusCode = plusCode6, CreatedOn = DateTime.Now, mode = 1, resolutionScale = 10, tileData = results });
                db.SaveChanges();
                pt.Stop(plusCode6);
                return File(results, "image/png");
            }

            pt.Stop(plusCode6);
            return File(existingResults.tileData, "image/png");
        }

        [HttpGet]
        [Route("/[controller]/DrawCell6Highres/{plusCode6}")]
        public FileContentResult DrawCell6Highres(string plusCode6)
        {
            //a PNG of a 6cell this way is roughly 124KB, and now takes ~400 seconds to generate. The game cannot possibly wait 6+ minutes for one of these to render.
            //Note: the optimization to the maptile process should mean this runs in ~100 seconds now. Much better, though still not on-demand feasible for a game.
            //An admin or a tester looking for all this data may find it useful though.
            PerformanceTracker pt = new PerformanceTracker("DrawCell6Highres");
            //Load terrain data for an 6cell, turn it into a bitmap
            var db = new PraxisContext();
            var existingResults = db.MapTiles.Where(mt => mt.PlusCode == plusCode6 && mt.resolutionScale == 11 && mt.mode == 1).FirstOrDefault();
            if (existingResults == null || existingResults.MapTileId == null)
            {
                //requires a list of colors to use, which might vary per app. Defined in AreaType
                GeoArea sixCell = OpenLocationCode.DecodeValid(plusCode6);
                var allPlaces = GetPlaces(sixCell, null, false, false);
                var results = MapTiles.DrawAreaMapTile(ref allPlaces, sixCell, 11);
                db.MapTiles.Add(new MapTile() { PlusCode = plusCode6, CreatedOn = DateTime.Now, mode = 1, resolutionScale = 11, tileData = results });
                db.SaveChanges();
                pt.Stop(plusCode6);
                return File(results, "image/png");
            }
            pt.Stop(plusCode6);
            return File(existingResults.tileData, "image/png");
        }

        [HttpGet]
        [Route("/[controller]/DrawFlex/{lat}/{lon}/{size}/{resolution}")]
        public FileContentResult DrawFlex(double lat, double lon, double size, int resolution)
        {
            //Flex endpoint doesnt save data to DB or cache stuff yet.
            PerformanceTracker pt = new PerformanceTracker("DrawFlex");
            GeoArea box = new GeoArea(new GeoPoint(lat - (size / 2), lon - (size / 2)), new GeoPoint(lat + (size / 2), lon + (size / 2)));
            var allPlaces = GetPlaces(box);
            byte[] results;
            if (resolution == 10)
                results = MapTiles.DrawAreaMapTile(ref allPlaces, box, 10);
            else
                results = MapTiles.DrawAreaMapTile(ref allPlaces, box, 11);
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

        //    var db = new PraxisContext();
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
            //attach a debug point here and read the results.

            //Exact point for area? or 10cell space to find trails too?
            var places = GetPlaces(new OpenLocationCode(lat, lon).Decode());
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

            var places = GetPlaces(box);  //All the places in this area
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(pointDesc);
            //This endpoint puts the whole 10-digit plus code (without the separator) at the start of the line. I can't guarentee that any digits are shared since this isn't a grid-bound endpoint.

            //Notes: 
            //StringBuilders isn't thread-safe, so each thread needs its own, and their results combined later.

            //This is sort of a magic formula I wandered into.
            // Sqrt(Size / resolution10 ) * 2 is my current logic.
            int splitcount = (int)Math.Floor(Math.Sqrt(size / resolutionCell10) * 2);
            List<MapData>[] placeArray;
            GeoArea[] areaArray;
            StringBuilder[] sbArray = new StringBuilder[splitcount * splitcount];
            Converters.SplitArea(box, splitcount, places, out placeArray, out areaArray);
            System.Threading.Tasks.Parallel.For(0, placeArray.Length, (i) =>
            {
                sbArray[i] = SearchArea(ref areaArray[i], ref placeArray[i], true);
            });

            foreach (StringBuilder sbPartial in sbArray)
                sb.Append(sbPartial.ToString());

            string results = sb.ToString();
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
            var places = GetPlaces(box);
            var plusCodeCoords = Converters.GeoAreaToCoordArray(box);
            var plusCodePoly = factory.CreatePolygon(plusCodeCoords);
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
            var places = GetPlaces(box);
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
            var places = GetPlaces(box);
            var coords = Converters.GeoAreaToCoordArray(box);
            var poly = factory.CreatePolygon(coords);
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
            var places = GetPlaces(box);
            var coords = Converters.GeoAreaToCoordArray(box);
            var poly = factory.CreatePolygon(coords);
            var results =  GetScoresForArea(poly, places);
            pt.Stop();
            return results;
        }

        [HttpGet]
        [Route("/[controller]/CalculateMapDataScore/{MapDataId}")]
        public string CalculateMapDataScore(long MapDataId)
        {
            PerformanceTracker pt = new PerformanceTracker("CalculateMapDataScore");
            var db = new PraxisContext();
            var places = db.MapData.Where(m => m.MapDataId == MapDataId).ToList();
            var results = GetScoresForFullArea(places);
            pt.Stop(MapDataId.ToString());
            return results;
        }
    }
}
