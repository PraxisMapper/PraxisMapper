using DatabaseAccess;
using Google.Common.Geometry;
using Google.OpenLocationCode;
using System;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using static DatabaseAccess.DbTables;
using static DatabaseAccess.MapSupport;

namespace PerformanceTestApp
{
    class PerfTestApp
    {
        static void Main(string[] args)
        {
            //This is for running and archiving performance tests on different code approaches.
            //PerformanceInfoEFCoreVsSproc();
            //S2VsPlusCode();
            SplitAreaValues();
        }

        public static List<CoordPair> GetRandomCoords(int count)
        {
            List<CoordPair> results = new List<CoordPair>();
            results.Capacity = count;

            for (int i = 0; i < count; i++)
                results.Add(MapSupport.GetRandomPoint());

            return results;
        }

        public static void PerformanceInfoEFCoreVsSproc()
        {
            int count = 100;
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            for (int i = 0; i < count; i++)
            {
                PerformanceTracker pt = new PerformanceTracker("test");
                pt.Stop();
            }
            sw.Stop();
            long EfCoreInsertTime = sw.ElapsedMilliseconds;

            sw.Restart();
            for (int i = 0; i < count; i++)
            {
                PerformanceTracker pt = new PerformanceTracker("test");
                pt.StopNoChangeTracking();
            }
            sw.Stop();
            long NoCTInsertTime = sw.ElapsedMilliseconds;
            
            sw.Restart();
            for (int i = 0; i < count; i++)
            {
                PerformanceTracker pt = new PerformanceTracker("test");
                pt.StopSproc();
            }
            sw.Stop();
            long SprocInsertTime = sw.ElapsedMilliseconds;

            Log.WriteLog("PerformanceTracker EntityFrameworkCore total  /average speed: " + EfCoreInsertTime + " / " + (EfCoreInsertTime / count) + "ms.");
            Log.WriteLog("PerformanceTracker EntityFrameworkCore NoChangeTracking total /average speed: " + NoCTInsertTime + " / "  + (NoCTInsertTime / count) + "ms.");
            Log.WriteLog("PerformanceTracker Sproc total / average speed: " + SprocInsertTime + " / " + (SprocInsertTime / count) + "ms.");
        }

        public static void S2VsPlusCode()
        {
            //Testing how fast the conversion between coords and areas is here.
            int count = 10000;
            var testPointList = GetRandomCoords(count);
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

            sw.Start();
            foreach(var coords in testPointList)
            {
                OpenLocationCode olc = new OpenLocationCode(coords.lat, coords.lon); //creates data from coords
                var area = olc.Decode(); //an area i can use for intersects() calls in the DB
            }
            sw.Stop();
            var PlusCodeConversion = sw.ElapsedMilliseconds;
            
            sw.Restart();
            foreach (var coords in testPointList)
            {
                S2LatLng s2 = S2LatLng.FromDegrees(coords.lat, coords.lon); //creates data from coords
                S2CellId c = S2CellId.FromLatLng(s2); //this gives a usable area, I think.
            }
            sw.Stop();
            var S2Conversion = sw.ElapsedMilliseconds;

            Log.WriteLog("PlusCode conversion total / average time: " + PlusCodeConversion +  " / " + (PlusCodeConversion / count) + " ms");
            Log.WriteLog("S2 conversion total / average time: " + S2Conversion + " / " + (S2Conversion / count) + " ms");

        }

        public static void SplitAreaValues()
        {
            //Load an area, see what value of splits is the fastest.
            //I currently think its 40.
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            //Pick a specific area for testing, since we want to compare the math.
            string plusCode6 = "8FW4V7"; //Eiffel Tower and surrounding area. Use for global data.
            var db = new DatabaseAccess.GpsExploreContext();
            //var factory = NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326); //SRID matches Plus code values.
            var places = MapSupport.GetPlaces(OpenLocationCode.DecodeValid(plusCode6));  //All the places in this 6-code
            var box = OpenLocationCode.DecodeValid(plusCode6);
            sw.Stop();
            Log.WriteLog("Pulling " + places.Count + " places in 6-cell took " + sw.ElapsedMilliseconds + "ms");

            int[] splitChecks = new int[] { 1, 2, 4, 8, 10, 20, 25, 32, 40, 80, 100 };
            foreach (int splitcount in splitChecks)
            {
                sw.Restart();
                List<MapData>[] placeArray;
                GeoArea[] areaArray;
                StringBuilder[] sbArray = new StringBuilder[splitcount * splitcount];
                MapSupport.SplitArea(box, splitcount, places, out placeArray, out areaArray);
                int loopSize = 400 / splitcount;
                System.Threading.Tasks.Parallel.For(0, placeArray.Length, (i) =>
                {
                    sbArray[i] = MapSupport.SearchArea(areaArray[i], ref placeArray[i]);
                });
                sw.Stop();
                Log.WriteLog("dividing map by " + splitcount + " took " + sw.ElapsedMilliseconds + " ms");
            }
        }
    }
}
