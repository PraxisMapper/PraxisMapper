using DatabaseAccess;
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
            PerformanceInfoEFCoreVsSproc();
        }

        public List<CoordPair> GetRandomCoords(int count)
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
                pt.StopSproc();
            }
            sw.Stop();
            long SprocInsertTime = sw.ElapsedMilliseconds;

            Log.WriteLog("PerformanceTracker EntityFrameworkCore average speed: " + (EfCoreInsertTime / count) + "ms.");
            Log.WriteLog("PerformanceTracker Sproc average speed: " + (SprocInsertTime / count) + "ms.");
        }
    }
}
