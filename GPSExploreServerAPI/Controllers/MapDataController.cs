using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading.Tasks;
using GPSExploreServerAPI.Classes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;
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
        public static List<MapData> getInfo(double lat, double lon)
        {
            //reusable function
            PerformanceTracker pt = new PerformanceTracker("getInfo");
            var db = new DatabaseAccess.GpsExploreContext();
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
            var location = factory.CreatePoint(new Coordinate(lon, lat));
            var places = db.MapData.Where(md => md.place.Contains(location)).ToList();
            pt.Stop();
            return places;
        }

        //This is where I do my calls to get which cells are which area type.
        [HttpGet]
        [Route("/[controller]/test")]
        public string TestDummyEndpoint()
        {
            //testing C# side code here, to make sure i have that sorted out.
            //For debug purposes to confirm the server is running and reachable.
            //i have lat/lon coords, whats there?
            //double lat = 41.511760;
            //double lon = -81.588722;
            //var db = new DatabaseAccess.GpsExploreContext();
            //var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
            //var location = factory.CreatePoint(new Coordinate(lon, lat));
            //var places = db.MapData.Where(md => md.place.Contains(location)).Select(md => new { md.name, md.type }).Distinct().ToList(); //This took about 2 seconds through C# code. Was nearly instant in SSMS
            ////Might make a sproc for this search, since it looked like it was faster, but that could be performance variance
            //return "OK" + places.Count();

            //normal debug results, to prove the server is running right.
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

            var r2 = results.Select(r => new { r.name, r.type }).Distinct().ToList();
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
            for (int i = 0; i < 500; i++)
            {
                //randomize lat and long
                double lat = r.NextDouble() * 90;
                double lon = r.NextDouble() * 180;
                sw.Restart();
                var db = new DatabaseAccess.GpsExploreContext();
                var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
                var location = factory.CreatePoint(new Coordinate(lon, lat));
                var places = db.MapData.Where(md => md.place.Contains(location)).Select(md => new { md.name, md.type }).Distinct().ToList(); //This took about 2 seconds through C# code. Was nearly instant in SSMS
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
