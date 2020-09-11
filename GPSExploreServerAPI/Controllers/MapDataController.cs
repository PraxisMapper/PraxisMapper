using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using DatabaseAccess;
using Google.OpenLocationCode;
using GPSExploreServerAPI.Classes;
using Microsoft.AspNetCore.Mvc;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using static DatabaseAccess.DbTables;

namespace GPSExploreServerAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class MapDataController : ControllerBase
    {
        //Manual edits:
        //none

        //TODO: figure out how to check on some of the pooly detected shapes I see in my area. Most are OK, some are not.

        //This takes the current point, and finds any geometry that contains that point.
        public static List<MapData> getInfo(double lat, double lon)
        {
            //reusable function
            PerformanceTracker pt = new PerformanceTracker("getInfo");
            var db = new DatabaseAccess.GpsExploreContext();
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
            var location = factory.CreatePoint(new Coordinate(lon, lat));
            var places = db.MapData.Where(md => md.place.Contains(location)).ToList();
            var pluscode = OpenLocationCode.Encode(lat, lon).Replace("+", "");
            var spoi = db.SinglePointsOfInterests.Where(sp => sp.PlusCode == pluscode).FirstOrDefault();
            if (spoi != null && spoi.SinglePointsOfInterestId != null)
            {
                //A single named place out-ranks the surrounding area.
                var tempPlace = new MapData() { name = spoi.name, type = spoi.NodeType };
                places.Clear();
                places.Add(tempPlace);
            }
            pt.Stop();
            return places;
        }

        //Make one of these that takes in the plus code, since simulator is stuck on lat/lon i cant move
        //This one does the math to get an area equal to a PlusCode 8 cell, then finds anything that intersects it.
        [HttpGet]
        [Route("/[controller]/cell8Info/{lat}/{lon}")]
        public string CellInfo(double lat, double lon)
        {
            //point 1 is southwest corner, point2 is northeast corner.
            //This works on an arbitrary sized area.
            //NOTE: the official library fails these partial codes, since it's always expecting a + and at least 2 digits after that.
            //so I may need to do some manual manipulation or editing of the libraries to do what I want.
            //Short codes trim off the front of the string for human use, i want to trim off the back for machine use.

            PerformanceTracker pt = new PerformanceTracker("Cell8info");
            var pluscode = new OpenLocationCode(lat, lon);
            var codeString = pluscode.Code.Substring(0, 8);
            var box = OpenLocationCode.DecodeValid(codeString);

            var db = new DatabaseAccess.GpsExploreContext();
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
            var cord1 = new Coordinate(box.Min.Longitude, box.Min.Latitude);
            var cord2 = new Coordinate(box.Min.Longitude, box.Max.Latitude);
            var cord3 = new Coordinate(box.Max.Longitude, box.Max.Latitude);
            var cord4 = new Coordinate(box.Max.Longitude, box.Min.Latitude);
            var cordSeq = new Coordinate[5] { cord4, cord3, cord2, cord1, cord4 };
            var location = factory.CreatePolygon(cordSeq);
            //var location = factory.CreatePoint(new Coordinate(lon, lat));
            //test list. Need to find an area that's not empty.
            var places = db.MapData.Where(md => md.place.Intersects(location)).ToList(); //areas have an intersection. This is correct upon testing a known area.
            var spoi = db.SinglePointsOfInterests.Where(sp => sp.PlusCode8 == codeString).ToList();

            StringBuilder sb = new StringBuilder();
            //pluscode8
            //pluscode2=name|type per entry
            sb.AppendLine(codeString);

            if (places.Count == 0 && spoi.Count == 0)
                return sb.ToString();

            //For this, i might need to dig through each plus code cell, but I have a much smaller set of data in memory. Might be faster ways to do this with a string array versus re-encoding OLC each loop?
            double resolution10 = .000125;
            for (double x = box.Min.Longitude; x <= box.Max.Longitude; x += resolution10)
            {
                for (double y = box.Min.Latitude; y <= box.Max.Latitude; y += resolution10)
                {
                    //also remember these coords start at the lower-left, so i can add the resolution to get the max bounds
                    var olc = new OpenLocationCode(y, x); //This takes lat, long, Coordinate takes X, Y. This line is correct.
                    var plusCode2 = olc.Code.Substring(9, 2);

                    //TODO: benchmark these 2 options. This function takes ~250ms sometimes, and I'm reasonably sure that's more SQL Server indexing issues than this code, but should confirm.
                    //Original option: create boxes to represent the 10code
                    var cordSeq2 = new Coordinate[5] { new Coordinate(x, y), new Coordinate(x + resolution10, y), new Coordinate(x + resolution10, y + resolution10), new Coordinate(x, y + resolution10), new Coordinate(x, y) };
                    var poly2 = factory.CreatePolygon(cordSeq2);
                    var entriesHere = places.Where(md => md.place.Intersects(poly2)).ToList();

                    //option 2: create a point in the middle of the 10cell, use that instead assuming its faster math.
                    //var point = olc.Decode().Center;
                    //var point2 = factory.CreatePoint(new Coordinate(point.Longitude, point.Latitude));
                    //var entriesHere2 = places.Where(md => md.place.Contains(point2)).ToList();

                    //First, if there's a single-point, return that.
                    var spoiToUser = spoi.Where(s => s.PlusCode == olc.Code.Replace("+", ""));
                    if (spoiToUser.Count() > 0)
                        sb.AppendLine(plusCode2 + "=" + spoiToUser.First().name + "|" + spoiToUser.First().NodeType);
                    else if (entriesHere.Count() == 0)
                    {
                        //sb.AppendLine(olc + "=|"); //nothing here. don't use up space/bandwidth with empty cells. The phone will track that.
                    }
                    else
                    {
                        if (entriesHere.Count() == 1)
                            sb.AppendLine(plusCode2 + "=" + entriesHere.First().name + "|" + entriesHere.First().type);
                        else
                        {
                            var named = entriesHere.Where(e => !string.IsNullOrWhiteSpace(e.name)).ToList();
                            if (named.Count() == 1)
                                sb.AppendLine(plusCode2 + "=" + named.First().name + "|" + named.First().type);
                            else
                                //This might be where types get sorted now.
                                //sorting should only occur if there's multiple overlapping entries to sort.
                                sb.AppendLine(plusCode2 + "=" + named.First().name + "|" + named.First().type);
                        }
                    }
                }
            }

            //pt.Stop(sb.ToString()); //testing data
            pt.Stop(); //faster
            return sb.ToString();
        }

        [HttpGet]
        [Route("/[controller]/cell6Info/{lat}/{lon}")]
        public string Cell6Info(double lat, double lon) //The current primary function used by the app.
        {
            //point 1 is southwest corner, point2 is northeast corner.
            //This works on an arbitrary sized area.
            //NOTE: the official library fails these partial codes, since it's always expecting a + and at least 2 digits after that.
            //so I may need to do some manual manipulation or editing of the libraries to do what I want.
            //Short codes trim off the front of the string for human use, i want to trim off the back for machine use.

            //It looks like there are some duplicate cells? I send over 4041 entries and the app saves 3588 and throws a few unique key errors on insert

            PerformanceTracker pt = new PerformanceTracker("Cell6info");
            var pluscode = new OpenLocationCode(lat, lon);
            var codeString6 = pluscode.Code.Substring(0, 6);
            var codeString8 = pluscode.Code.Substring(0, 8);
            var box = OpenLocationCode.DecodeValid(codeString6);

            var db = new DatabaseAccess.GpsExploreContext();
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
            var cord1 = new Coordinate(box.Min.Longitude, box.Min.Latitude);
            var cord2 = new Coordinate(box.Min.Longitude, box.Max.Latitude);
            var cord3 = new Coordinate(box.Max.Longitude, box.Max.Latitude);
            var cord4 = new Coordinate(box.Max.Longitude, box.Min.Latitude);
            var cordSeq = new Coordinate[5] { cord4, cord3, cord2, cord1, cord4 };
            var location = factory.CreatePolygon(cordSeq);
            var places = db.MapData.Where(md => md.place.Intersects(location)).ToList(); //areas have an intersection. This is correct upon testing a known area. Contains() might also be needed, but it requires MakeValid to be called, and doesn't seem to help results quality.. // || md.place.Contains(location)
            //var indexedPlaces = places.Select(md => new {md.place,  md.name, md.type, indexed = new NetTopologySuite.Algorithm.Locate.IndexedPointInAreaLocator(md.place) }); //this should be half the time my current lookups are, but its not?
            var spoi = db.SinglePointsOfInterests.Where(sp => sp.PlusCode8 == codeString8).ToList();

            StringBuilder sb = new StringBuilder();
            //pluscode6 //same syntax as 8info, but for a 6
            //pluscode10|name|type  //allows for less string processing on the phone side but makes data transmit slighlty bigger
            sb.AppendLine(codeString6);

            if (places.Count == 0 && spoi.Count == 0)
                return sb.ToString();

            //add spois first, instead of checking 400 times if the list has entries. The app will load the first entry for each 10cell, and fail to insert a duplicate later in the return value.
            foreach (var s in spoi)
                sb.AppendLine(s.PlusCode.Substring(6, 4) + "|" + s.name + "|" + s.NodeType);

            //This is every 10code in a 6code.
            //For this, i might need to dig through each  plus code cell, but I have a much smaller set of data in memory. Might be faster ways to do this with a string array versus re-encoding OLC each loop?
            double resolution10 = .000125; //as defined
            //double resolution10 = (box.Max.Longitude - box.Min.Longitude) / 400; //practical answer, is slightly smaller at my long. could be double rounding error
            //double resolution102 = (box.Max.Latitude - box.Min.Latitude) / 400; //practical answer, is slightly larget at my lat. could be double rounding error.
            for (double x = box.Min.Longitude; x <= box.Max.Longitude; x += resolution10)
            {
                for (double y = box.Min.Latitude; y <= box.Max.Latitude; y += resolution10)
                {
                    

                    //TODO: benchmark these 2 options. A quick check seems to suggest that this one is actually faster and more complete.
                    //Original option: create boxes to represent the 10code
                    //This one takes ~930ms warm, is the most consistent in time taken.
                    var cordSeq2 = new Coordinate[5] { new Coordinate(x, y), new Coordinate(x + resolution10, y), new Coordinate(x + resolution10, y + resolution10), new Coordinate(x, y + resolution10), new Coordinate(x, y) };
                    var poly2 = factory.CreatePolygon(cordSeq2);
                    var entriesHere = places.Where(md => md.place.Intersects(poly2)).ToList();

                    //option 2: create a point in the middle of the 10cell, use that instead assuming its faster math.
                    //In home 6-cell, this results in almost 2x the results decoding OLC again. Try just x and y.
                    //Local park looks square this way, instead of slightly taller on one side.
                    //This takes 1250ms warm, is occasionally faster than option 1 but not consistently
                    //var point2 = factory.CreatePoint(new Coordinate(x, y));
                    //var entriesHere = places.Where(md => md.place.Contains(point2)).ToList();

                    //option 3 is the built-in algorithm. Is the fastest in benchmark testing. Not in actual lookups? 200ms faster than Option 1, usually, in perfTest but way slower here.
                    //var point3 = new Coordinate(x, y);
                    //var entriesHere = indexedPlaces.Where(i => i.indexed.Locate(point3) == Location.Interior).ToList();


                    if (entriesHere.Count() == 0)
                    {
                        continue;
                    }
                    else
                    {
                        //Generally, if there's a smaller shape inside a bigger shape, the smaller one should take priority. Also going to look at only sending over last 4 on each line for server speed.
                        var olc = new OpenLocationCode(y, x).CodeDigits.Substring(6, 4); //This takes lat, long, Coordinate takes X, Y. This line is correct.
                        var smallest = entriesHere.Where(e => e.place.Area == entriesHere.Min(e => e.place.Area)).First();
                        sb.AppendLine(olc + "|" + smallest.name + "|" + smallest.type);
                    }
                }
            }

            pt.Stop();
            return sb.ToString();
        }

        [HttpGet]
        [Route("/[controller]/test")]
        public string TestDummyEndpoint()
        {
            //For debug purposes to confirm the server is running and reachable.
            return "OK";
        }

        [HttpGet]
        [Route("/[controller]/cellData/{pluscode}")]
        public string CellData(string pluscode)
        {
            //This was the first check on a single 10cell area. It's reasonably fast, but Solar2D tends to choke trying to process that many network requests.
            Google.OpenLocationCode.OpenLocationCode olc = new Google.OpenLocationCode.OpenLocationCode(pluscode);
            var decode = olc.Decode();
            double lat = decode.CenterLatitude;
            double lon = decode.CenterLongitude;

            var results = getInfo(lat, lon);
            if (results.Count() == 0)
                return pluscode; //speed shortcut,no = separator indicates no special data.

            if (results.Where(r => !string.IsNullOrEmpty(r.name)).Count() == 1)
                results = results.Where(r => !string.IsNullOrEmpty(r.name)).ToList();

            var r2 = results.Select(r => new { r.name, r.type }).Distinct().FirstOrDefault();

            string strData = pluscode + "=" + string.Join("=", results.Select(r => r.name + "|" + r.type));
            return strData;
        }

        [HttpGet]
        [Route("/[controller]/cellData/{lat}/{lon}")]
        public string CellData(double lat, double lon)
        {
            var results = getInfo(lat, lon);
            string strData = string.Join("=", results.Select(r => r.name + "|" + r.type));
            return strData;
        }

        [HttpGet]
        [Route("/[controller]/PerfTest")]
        public void PerfTest()
        {
            //testing C# side code here, to make sure i have that sorted out.
            //For debug purposes to confirm the server is running and reachable.
            //i have lat/lon coords, whats there?
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            Random r = new Random();

            List<long> intersectsPolygonRuntimes = new List<long>(50);
            List<long> containsPointRuntimes = new List<long>(50);
            List<long> AlgorithmRuntimes = new List<long>(50);
            List<long> precachedAlgorithmRuntimes = new List<long>(50);

            //tryint to determine the fastest way to search areas. Pull a 6-cell's worth of data from the DB, then parse it into 10cells.
            //Option 1: make a box, check Intersects.
            //Option 2: make a point, check Contains. (NOTE: a polygon does not Contain() its boundaries, so a point directly on a boundary line will not be identified)
            //Option 3: try NetTopologySuite.Algorithm.Locate.IndexedPointInAreaLocator ?
            //Option 4: consider using Contains against something like NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory().Prepare(geom) instead of just Place? This might be outdated

            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values. //share this here, so i compare the actual algorithms instead of this boilerplate, mandatory entry.
            var db = new GpsExploreContext();

            for (int i = 0; i < 50; i++)
            {
                //randomize lat and long to somewhere in Ohio.
                //42, -80 NE
                //38, -84 SW
                //so 38 + (0-4), -84 = (0-4) coords.
                double lat = 38 + (r.NextDouble() * 4);
                double lon = -84 + ( r.NextDouble() * 4);
                var olc = OpenLocationCode.Encode(lat, lon);
                var codeString = olc.Substring(0, 6);
                sw.Restart();
                var box = OpenLocationCode.DecodeValid(codeString);
                var cord1 = new Coordinate(box.Min.Longitude, box.Min.Latitude);
                var cord2 = new Coordinate(box.Min.Longitude, box.Max.Latitude);
                var cord3 = new Coordinate(box.Max.Longitude, box.Max.Latitude);
                var cord4 = new Coordinate(box.Max.Longitude, box.Min.Latitude);
                var cordSeq = new Coordinate[5] { cord4, cord3, cord2, cord1, cord4 };
                var location = factory.CreatePolygon(cordSeq); //the 6 cell.

                var places = db.MapData.Where(md => md.place.Intersects(location)).ToList();
                double resolution10 = .000125; //as defined
                for (double x = box.Min.Longitude; x <= box.Max.Longitude; x += resolution10)
                {
                    for (double y = box.Min.Latitude; y <= box.Max.Latitude; y += resolution10)
                    {
                        //also remember these coords start at the lower-left, so i can add the resolution to get the max bounds
                        var olcInner = new OpenLocationCode(y, x); //This takes lat, long, Coordinate takes X, Y. This line is correct.

                        //TODO: benchmark these 2 options. A quick check seems to suggest that this one is actually faster and more complete.
                        //Original option: create boxes to represent the 10code
                        //This one takes ~930ms warm
                        var cordSeq2 = new Coordinate[5] { new Coordinate(x, y), new Coordinate(x + resolution10, y), new Coordinate(x + resolution10, y + resolution10), new Coordinate(x, y + resolution10), new Coordinate(x, y) };
                        var poly2 = factory.CreatePolygon(cordSeq2);
                        var entriesHere = places.Where(md => md.place.Intersects(poly2)).ToList();

                    }
                }
                sw.Stop(); //measuring time it takes to parse a 6-cell down to 10-cells.and wou
                intersectsPolygonRuntimes.Add(sw.ElapsedMilliseconds);  
            }

            for (int i = 0; i < 50; i++)
            {
                //randomize lat and long to somewhere in Ohio.
                //42, -80 NE
                //38, -84 SW
                //so 38 + (0-4), -84 = (0-4) coords.
                double lat = 38 + (r.NextDouble() * 4);
                double lon = -84 + (r.NextDouble() * 4);
                var olc = OpenLocationCode.Encode(lat, lon);
                var codeString = olc.Substring(0, 6);
                sw.Restart();
                var box = OpenLocationCode.DecodeValid(codeString);
                var cord1 = new Coordinate(box.Min.Longitude, box.Min.Latitude);
                var cord2 = new Coordinate(box.Min.Longitude, box.Max.Latitude);
                var cord3 = new Coordinate(box.Max.Longitude, box.Max.Latitude);
                var cord4 = new Coordinate(box.Max.Longitude, box.Min.Latitude);
                var cordSeq = new Coordinate[5] { cord4, cord3, cord2, cord1, cord4 };
                var location = factory.CreatePolygon(cordSeq); //the 6 cell.

                var places = db.MapData.Where(md => md.place.Intersects(location)).ToList();
                double resolution10 = .000125; //as defined
                for (double x = box.Min.Longitude; x <= box.Max.Longitude; x += resolution10)
                {
                    for (double y = box.Min.Latitude; y <= box.Max.Latitude; y += resolution10)
                    {
                        //Option 2, is Contains on a point faster?
                        var location2 = factory.CreatePoint(new Coordinate(x, y));
                        var places2 = places.Where(md => md.place.Contains(location)).ToList();

                    }
                }
                sw.Stop(); //measuring time it takes to parse a 6-cell down to 10-cells.and wou
                containsPointRuntimes.Add(sw.ElapsedMilliseconds);
            }

            for (int i = 0; i < 50; i++)
            {
                //randomize lat and long to somewhere in Ohio.
                //42, -80 NE
                //38, -84 SW
                //so 38 + (0-4), -84 = (0-4) coords.
                double lat = 38 + (r.NextDouble() * 4);
                double lon = -84 + (r.NextDouble() * 4);
                var olc = OpenLocationCode.Encode(lat, lon);
                var codeString = olc.Substring(0, 6);
                sw.Restart();
                var box = OpenLocationCode.DecodeValid(codeString);
                var cord1 = new Coordinate(box.Min.Longitude, box.Min.Latitude);
                var cord2 = new Coordinate(box.Min.Longitude, box.Max.Latitude);
                var cord3 = new Coordinate(box.Max.Longitude, box.Max.Latitude);
                var cord4 = new Coordinate(box.Max.Longitude, box.Min.Latitude);
                var cordSeq = new Coordinate[5] { cord4, cord3, cord2, cord1, cord4 };
                var location = factory.CreatePolygon(cordSeq); //the 6 cell.

                var places = db.MapData.Where(md => md.place.Intersects(location)).ToList();
                var indexedIn = places.Select(md => new { md, Index = new NetTopologySuite.Algorithm.Locate.IndexedPointInAreaLocator(md.place) }).ToList();
                double resolution10 = .000125; //as defined
                for (double x = box.Min.Longitude; x <= box.Max.Longitude; x += resolution10)
                {
                    for (double y = box.Min.Latitude; y <= box.Max.Latitude; y += resolution10)
                    {
                        //Option 2, is Contains on a point faster?
                        var location2 = new Coordinate(x, y);
                        var places3 = indexedIn.Where(i => i.Index.Locate(location2) == Location.Interior);
                    }
                }
                sw.Stop(); //measuring time it takes to parse a 6-cell down to 10-cells.and wou
                AlgorithmRuntimes.Add(sw.ElapsedMilliseconds);
            }

            for (int i = 0; i < 50; i++)
            {
                //randomize lat and long to somewhere in Ohio.
                //42, -80 NE
                //38, -84 SW
                //so 38 + (0-4), -84 = (0-4) coords.
                double lat = 38 + (r.NextDouble() * 4);
                double lon = -84 + (r.NextDouble() * 4);
                var olc = OpenLocationCode.Encode(lat, lon);
                var codeString = olc.Substring(0, 6);
                sw.Restart();
                var box = OpenLocationCode.DecodeValid(codeString);
                var cord1 = new Coordinate(box.Min.Longitude, box.Min.Latitude);
                var cord2 = new Coordinate(box.Min.Longitude, box.Max.Latitude);
                var cord3 = new Coordinate(box.Max.Longitude, box.Max.Latitude);
                var cord4 = new Coordinate(box.Max.Longitude, box.Min.Latitude);
                var cordSeq = new Coordinate[5] { cord4, cord3, cord2, cord1, cord4 };
                var location = factory.CreatePolygon(cordSeq); //the 6 cell.

                //var places = db.MapData.Where(md => md.place.Intersects(location)).ToList();
                var indexedIn = db.MapData.Where(md => md.place.Contains(location)).Select(md => new NetTopologySuite.Algorithm.Locate.IndexedPointInAreaLocator(md.place)).ToList();
                var fakeCoord = new Coordinate(lon, lat);
                foreach (var ii in indexedIn)
                    ii.Locate(fakeCoord); //force index creation on all items now instead of later.

                double resolution10 = .000125; //as defined
                for (double x = box.Min.Longitude; x <= box.Max.Longitude; x += resolution10)
                {
                    for (double y = box.Min.Latitude; y <= box.Max.Latitude; y += resolution10)
                    {
                        //Option 2, is Contains on a point faster?
                        var location2 = new Coordinate(x, y);
                        var places3 = indexedIn.Where(i => i.Locate(location2) == Location.Interior);
                    }
                }
                sw.Stop(); //measuring time it takes to parse a 6-cell down to 10-cells.and wou
                precachedAlgorithmRuntimes.Add(sw.ElapsedMilliseconds);
            }

            var a = AlgorithmRuntimes.Average(); //557-680ms. half my current idea, but why is it so slow when i try on the main function?
            var b = intersectsPolygonRuntimes.Average(); //967ms. Most consistent results.
            var c = containsPointRuntimes.Average(); //700-1678ms. That's extremely swingy.
            var d = precachedAlgorithmRuntimes.Average(); //778ms, is slower than algorithmruntimes



            //This doesn;t work the way i want, this logic would be done on the SQL Server side via indexing/
            //yes this can, on a bigger scale. I pull in all the areas in a 6-cell, then determine what thing is in each 10cell using the indexedPointInAreaLocator function.
            

            //previous test C# search logic speed. This takes 1ms after warming up.
            //for (int i = 0; i < 50000; i++)
            //{
            //    //randomize lat and long
            //    double lat = r.NextDouble() * 90 * (r.Next() % 2 == 0 ? 1 : -1);
            //    double lon = r.NextDouble() * 180 * (r.Next() % 2 == 0 ? 1 : -1);
            //    sw.Restart();
            //    var location = factory.CreatePoint(new Coordinate(lon, lat));
            //    var places = db.MapData.Where(md => md.place.Contains(location)).Select(md => new { md.name, md.type }).Distinct().ToList();
            //    //Might make a sproc for this search, since it looked like it was faster, but that could be performance variance

            //    sw.Stop();
            //    //cSharpRuntimes.Add(sw.ElapsedMilliseconds);
            //}

            //return cSharpRuntimes.Average().ToString();

            return;

        }
    }
}
