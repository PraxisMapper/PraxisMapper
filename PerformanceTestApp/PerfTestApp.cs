using DatabaseAccess;
using Google.Common.Geometry;
using Google.OpenLocationCode;
using System;
using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;
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
    }
}
