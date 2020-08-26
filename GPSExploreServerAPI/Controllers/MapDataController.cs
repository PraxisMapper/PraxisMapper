using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.ComTypes;
using System.Threading.Tasks;
using GPSExploreServerAPI.Classes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Overlay.Validate;

namespace GPSExploreServerAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class MapDataController : ControllerBase
    {
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
        [Route("/[controller]/cellData/{pluscode8}")]
        public string CellData(string pluscode8)
        {
            //I have a plus code, what data's there?
            return "OK";
        }

        [HttpGet]
        [Route("/[controller]/cellData/{lat}/{lon}")]
        public string CellData(double lat, double lon)
        {
            //i have lat/lon coords, whats there?
            var db = new DatabaseAccess.GpsExploreContext();
            var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
            var location = factory.CreatePoint(new Coordinate(lon, lat));
            var places = db.MapData.Where(md => md.place.Contains(location)).Select(md => new { md.name, md.type }).Distinct().ToList();
            return "OK" + places.Count();
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
            List<long> sprocRuntimes = new List<long>(500);

            //test C# search logic speed.
            for (int i = 0; i < 500; i++)
            {
                //randomize lat and long
                double lat = r.NextDouble() * 90;
                double lon = r.NextDouble() * 180;
                sw.Start();
                var db = new DatabaseAccess.GpsExploreContext();
                var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
                var location = factory.CreatePoint(new Coordinate(lon, lat));
                var places = db.MapData.Where(md => md.place.Contains(location)).Select(md => new { md.name, md.type }).Distinct().ToList(); //This took about 2 seconds through C# code. Was nearly instant in SSMS
                //Might make a sproc for this search, since it looked like it was faster, but that could be performance variance

                sw.Stop();
                cSharpRuntimes.Add(sw.ElapsedMilliseconds);
            }

            //test SQL Sproc speed
            for (int i = 0; i < 500; i++)
            {
                //randomize lat and long
                double lat = r.NextDouble() * 90;
                double lon = r.NextDouble() * 180;
                sw.Start();
                var db = new DatabaseAccess.GpsExploreContext();
                var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
                var location = factory.CreatePoint(new Coordinate(lon, lat));
                var places = db.MapData.Where(md => md.place.Contains(location)).Select(md => new { md.name, md.type }).Distinct().ToList(); //This took about 2 seconds through C# code. Was nearly instant in SSMS
                //Might make a sproc for this search, since it looked like it was faster, but that could be performance variance

                sw.Stop();
                cSharpRuntimes.Add(sw.ElapsedMilliseconds);
            }


            return "OK";

        }




    }
}
