using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;
using Google.OpenLocationCode;
using GPSExploreServerAPI.Classes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;
using NetTopologySuite.Operation.Overlay.Validate;
using static DatabaseAccess.DbTables;

namespace GPSExploreServerAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class MapDataController : ControllerBase
    {
        //Manual edits:
        //way ID 117019427 is the issue - Bay of Fundi polygon seems to be inside out, registers everywhere. Deleted.
        //Added plusCode and index to SinglePointsOfInterest table since I uploaded it to S3. Re-run those commands. Might want to change that to 2 columns

        //This takes the current point, and finds any geometry that contains that point.
        public static List<MapData> getInfo(double lat, double lon)
        {
            //reusable function
            PerformanceTracker pt = new PerformanceTracker("getInfo");
            var db = new DatabaseAccess.GpsExploreContext();
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
            var location = factory.CreatePoint(new Coordinate(lon, lat));
            var places = db.MapData.Where(md => md.place.Contains(location)).ToList(); //Distinct occasionally throws errors here.
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
        //TODO optimization: return first 8 digits as an initial string, then 2 digits for each 10cell instead of each full 10cell value.
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
            //var places3 = db.MapData.Where(md => md.place.Contains(location)).ToList(); //Contains only takes things totally enclosed. Might want Intersects
            //var places5 = db.MapData.Where(md => md.place.Crosses(location)).ToList(); //says its invalid. This is PROBABLY what I want (Intersection is smaller than both areas, intersection is interior to both)?
            //var places6 = db.MapData.Where(md => md.place.CoveredBy(location)).ToList(); //errors out
            //var places7 = db.MapData.Where(md => md.place.Overlaps(location)).ToList(); //effectively both instances are identical areas. Not what I wanted.
            //var places8 = db.MapData.Where(md => md.place.Touches(location)).ToList(); //Points are shared but areas are not.
            //var places9 = db.MapData.Where(md => md.place.Within(location)).ToList(); //area 1 completely contained by area 2
            //var places10 = db.MapData.Where(md => location.Within(md.place)).ToList(); //area 1 completely contained by area 2
            //var places11 = db.MapData.Where(md => location.CoveredBy(md.place)).ToList(); //errors out

            //var places2 = db.MapData.Where(md => location.Intersects(md.place)).ToList(); //option 2, in case this math is picky
            //var places4 = db.MapData.Where(md => location.Contains(md.place)).ToList(); //Contains only takes things totally enclosed. Might want Intersects

            var spoi = db.SinglePointsOfInterests.Where(sp => sp.PlusCode8 == codeString).ToList();

            StringBuilder sb = new StringBuilder();
            //pluscode=name|type per entry
            
            //might want a faster loop if an 8cell has 0 interesting items.

            //For this, i might need to dig through each  plus code cell, but I have a much smaller set of data in memory. Might be faster ways to do this with a string array versus re-encoding OLC each loop?
            double resolution10 = .000125;
            for(double x = box.Min.Longitude; x== box.Max.Longitude; x += resolution10)
            {
                for (double y = box.Min.Latitude; y == box.Max.Latitude; y += resolution10)
                {
                    //also remember these coords start at the lower-left, so i can add the resolution to get the max bounds
                    var olc = new OpenLocationCode(x, y);
                    var cordSeq2 = new Coordinate[5] {new Coordinate(x, y), new Coordinate(x + resolution10, y), new Coordinate(x + resolution10, y + resolution10), new Coordinate(x, y + resolution10), new Coordinate(x, y)};
                    var poly2 = factory.CreatePolygon(cordSeq2);
                    var entriesHere = places.Where(md => md.place.Intersects(poly2)).ToList();
                    
                    //First, if there's a single-point, return that.
                    var spoiToUser = spoi.Where(s => s.PlusCode == olc.Code.Replace("+", ""));
                    if (spoiToUser.Count() > 0)
                        sb.AppendLine(spoiToUser.First().PlusCode + "=" + spoiToUser.First().name + "|" + spoiToUser.First().NodeType);
                    else if (entriesHere.Count() == 0)
                        sb.AppendLine(olc + "=|"); //nothing here.
                    else
                    {
                        if (entriesHere.Count() == 1)
                            sb.AppendLine(olc + "=" + entriesHere.First().name + "|" + entriesHere.First().type);
                        else
                        {
                            var named = entriesHere.Where(e => !string.IsNullOrWhiteSpace(e.name)).ToList();
                            if (named.Count() == 1)
                                sb.AppendLine(olc + "=" + named.First().name + "|" + named.First().type);
                            else
                                //This might be where types get sorted now.
                                //sorting should only occur if there's multiple overlapping entries to sort.
                                sb.AppendLine(olc + "=" + named.First().name + "|" + named.First().type);
                        }
                    }
                }
            }

            pt.Stop();
            return sb.ToString();
        }

        //This is where I do my calls to get which cells are which area type.
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
            //I have a plus code, what data's there? Should work for 8 and 10 code data? is fast enough that I can probably always pass in 10codes.
            Google.OpenLocationCode.OpenLocationCode olc = new Google.OpenLocationCode.OpenLocationCode(pluscode);
            var decode = olc.Decode();
            double lat = decode.CenterLatitude;
            double lon = decode.CenterLongitude;

            var results = getInfo(lat, lon);
            if (results.Count() == 0)
                return pluscode; //speed shortcut,no = separator indicates no special data.

            //The app only uses 1 of these results, so I sort them here.
            //Should probably prioritize named locations first.
            if (results.Where(r => !string.IsNullOrEmpty(r.name)).Count() == 1)
                results = results.Where(r => !string.IsNullOrEmpty(r.name)).ToList();

            //TODO: sort by type, so important types have priority displaying?
            //or sort by area size, so smaller areas inside a bigger area count?

            //Current sorting rules:
            //1: if only one entry has a name, it gets priority over unnamed areas.
            //last: the first out out of the database otherwise gets priority.

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
        public string PerfTest()
        {
            //testing C# side code here, to make sure i have that sorted out.
            //For debug purposes to confirm the server is running and reachable.
            //i have lat/lon coords, whats there?
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            Random r = new Random();

            List<long> cSharpRuntimes = new List<long>(500);
            //List<long> sprocRuntimes = new List<long>(500); //C# is fast enough, dont need the sproc after all.

            //test C# search logic speed. This takes 1ms after warming up.
            for (int i = 0; i < 50000; i++)
            {
                //randomize lat and long
                double lat = r.NextDouble() * 90 * (r.Next() % 2 == 0 ? 1 : -1);
                double lon = r.NextDouble() * 180 * (r.Next() % 2 == 0 ? 1 : -1);
                sw.Restart();
                var db = new DatabaseAccess.GpsExploreContext();
                var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
                var location = factory.CreatePoint(new Coordinate(lon, lat));
                var places = db.MapData.Where(md => md.place.Contains(location)).Select(md => new { md.name, md.type }).Distinct().ToList();
                //Might make a sproc for this search, since it looked like it was faster, but that could be performance variance

                sw.Stop();
                cSharpRuntimes.Add(sw.ElapsedMilliseconds);
            }

            ////test SQL Sproc speed
            //for (int i = 0; i < 500; i++)
            //{
            //    //randomize lat and long
            //    double lat = r.NextDouble() * 90;
            //    double lon = r.NextDouble() * 180;
            //    sw.Start();
            //    var db = new DatabaseAccess.GpsExploreContext();
            //    var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
            //    var location = factory.CreatePoint(new Coordinate(lon, lat));
            //    //var places = db.MapData.FromSqlRaw("lookupMapData " + lat.ToString() + " , " + lon.ToString()).ToList(); This isn't the right setup for this.
            //    //Might make a sproc for this search, since it looked like it was faster, but that could be performance variance

            //    sw.Stop();
            //    sprocRuntimes.Add(sw.ElapsedMilliseconds);
            //}


            return cSharpRuntimes.Average().ToString();

        }




    }
}
