using System;
using System.Collections.Generic;
using System.Linq;
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
        //Manual map edits:
        //none

        //TODO:
        //implement caching, so a calculated 6cell's results can be reused until the app pool is reset or memory is freed.     
        //Make an endpoint that takes in the plus code, as a testing convenience

        public Coordinate[] MakeBox(CodeArea plusCodeArea)
        {
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
            var cord1 = new Coordinate(plusCodeArea.Min.Longitude, plusCodeArea.Min.Latitude);
            var cord2 = new Coordinate(plusCodeArea.Min.Longitude, plusCodeArea.Max.Latitude);
            var cord3 = new Coordinate(plusCodeArea.Max.Longitude, plusCodeArea.Max.Latitude);
            var cord4 = new Coordinate(plusCodeArea.Max.Longitude, plusCodeArea.Min.Latitude);
            var cordSeq = new Coordinate[5] { cord4, cord3, cord2, cord1, cord4 };

            return cordSeq;
        }

        //This one does the math to get an area equal to a PlusCode 8 cell, then finds anything that intersects it.
        [HttpGet]
        [Route("/[controller]/cell8Info/{lat}/{lon}")]
        public string CellInfo(double lat, double lon)
        {
            //TODO: if I return to this 8-cell style of logic, update this code to match the 6-cell version.
            //point 1 is southwest corner, point2 is northeast corner.
            //This works on an arbitrary sized area.
            //NOTE: the official library fails these partial codes, since it's always expecting a + and at least 2 digits after that.
            //I've made a function public that returns the info I need from that library.
            
            PerformanceTracker pt = new PerformanceTracker("Cell8info");
            var pluscode = new OpenLocationCode(lat, lon);
            var codeString = pluscode.Code.Substring(0, 8);
            var box = OpenLocationCode.DecodeValid(codeString);

            var db = new DatabaseAccess.GpsExploreContext();
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
            var cordSeq = MakeBox(box);
            var location = factory.CreatePolygon(cordSeq);
            var places = db.MapData.Where(md => md.place.Intersects(location)).ToList(); //Intersects is the correct function for this.
            var spoi = db.SinglePointsOfInterests.Where(sp => sp.PlusCode8 == codeString).ToList();

            StringBuilder sb = new StringBuilder();
            //pluscode8
            //pluscode2=name|type per entry
            sb.AppendLine(codeString);

            if (places.Count == 0 && spoi.Count == 0)
                return sb.ToString();

            double resolution10 = .000125;
            for (double x = box.Min.Longitude; x <= box.Max.Longitude; x += resolution10)
            {
                for (double y = box.Min.Latitude; y <= box.Max.Latitude; y += resolution10)
                {
                    //also remember these coords start at the lower-left, so i can add the resolution to get the max bounds
                    var olc = new OpenLocationCode(y, x); //This takes lat, long, Coordinate takes X, Y. This line is correct.
                    var plusCode2 = olc.Code.Substring(9, 2);

                    var cordSeq2 = new Coordinate[5] { new Coordinate(x, y), new Coordinate(x + resolution10, y), new Coordinate(x + resolution10, y + resolution10), new Coordinate(x, y + resolution10), new Coordinate(x, y) };
                    var poly2 = factory.CreatePolygon(cordSeq2);
                    var entriesHere = places.Where(md => md.place.Intersects(poly2)).ToList();

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

            pt.Stop();
            return sb.ToString();
        }

        [HttpGet]
        [Route("/[controller]/cell6Info/{lat}/{lon}")]
        public string Cell6Info(double lat, double lon) //The current primary function used by the app.
        {
            //point 1 is southwest corner, point2 is northeast corner.
            //NOTE: the official library fails these partial codes, since it's always expecting a + and at least 2 digits after that.
            //I made a function public to get the data I wanted from the official library.

            //Now that I have waterways included, this takes ~3 seconds on nearby places instead of 1. May need to re-evaluate using the algorithm now.

            PerformanceTracker pt = new PerformanceTracker("Cell6info");
            var pluscode = new OpenLocationCode(lat, lon);
            var codeString6 = pluscode.Code.Substring(0, 6);
            var codeString8 = pluscode.Code.Substring(0, 8);
            var box = OpenLocationCode.DecodeValid(codeString6);

            var db = new DatabaseAccess.GpsExploreContext();
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
            var cordSeq = MakeBox(box);
            var location = factory.CreatePolygon(cordSeq);
            var places = db.MapData.Where(md => md.place.Intersects(location)).ToList(); 
            var spoi = db.SinglePointsOfInterests.Where(sp => sp.PlusCode6 == codeString6).ToList();

            StringBuilder sb = new StringBuilder();
            //pluscode6 //first 6 digits of this pluscode. each line below is the last 4 that have an area type.
            //pluscode4|name|type  //less data transmitted, an extra string concat per entry phone-side.
            sb.AppendLine(codeString6);

            if (places.Count == 0 && spoi.Count == 0)
                return sb.ToString();

            //add spois first, instead of checking 400 times if the list has entries. The app will load the first entry for each 10cell, and fail to insert a duplicate later in the return value.
            foreach (var s in spoi)
                sb.AppendLine(s.PlusCode.Substring(6, 4) + "|" + s.name + "|" + s.NodeType);

            //Notes:
            //waterfall 6-cell has 222 entries in Places to check and 11 spoi, takes ~6 seconds. 55674 cells to send back. 2MB unzipped
            //home 6-cell has 287 places to check and 1 spoi, takes ~2.5 seconds. 13534 cells to send back. 340kb unzipped
            //Need to figure out how to reduce those numbers some. And why the first takes so much longer to process
            //making xx a parallel for loop cuts execution times to ~2.5 and ~1 second, respectively. Good easy improvement.
            
            //This is every 10code in a 6code. I count to 400 to avoid rounding errors on NE edges of a 6-cell resulting in empty lines.
            //For this, i might need to dig through each plus code cell, but I have a much smaller set of data in memory. Might be faster ways to do this with a string array versus re-encoding OLC each loop?
            double resolution10 = .000125; //as defined
            //for (double xx = 0; xx < 400; xx += 1)
            System.Threading.Tasks.Parallel.For(0, 400, xx =>
            {
                for (double yy = 0; yy < 400; yy++)
                {
                    double x = box.Min.Longitude + (resolution10 * xx);
                    double y = box.Min.Latitude + (resolution10 * yy);

                    //The only remaining optimzation here is to attempt to find a function that's faster than Intersects on linestrings, and if there is one to check when to use which.
                    var cordSeq2 = new Coordinate[5] { new Coordinate(x, y), new Coordinate(x + resolution10, y), new Coordinate(x + resolution10, y + resolution10), new Coordinate(x, y + resolution10), new Coordinate(x, y) };
                    var poly2 = factory.CreatePolygon(cordSeq2);
                    var entriesHere = places.Where(md => md.place.Intersects(poly2)).ToList();

                    if (entriesHere.Count() == 0)
                    {
                        continue;
                    }
                    else
                    {
                        //Generally, if there's a smaller shape inside a bigger shape, the smaller one should take priority.
                        var olc = new OpenLocationCode(y, x).CodeDigits.Substring(6, 4); //This takes lat, long, Coordinate takes X, Y. This line is correct.
                        var smallest = entriesHere.Where(e => e.place.Area == entriesHere.Min(e => e.place.Area)).First();
                        sb.AppendLine(olc + "|" + smallest.name + "|" + smallest.type);
                    }
                }
            });

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


            return;

        }
    }
}
