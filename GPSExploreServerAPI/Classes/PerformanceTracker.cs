using GPSExploreServerAPI.Database;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace GPSExploreServerAPI.Classes
{
    public class PerformanceTracker
    {
        Database.PerformanceInfo pi = new Database.PerformanceInfo();
        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        public PerformanceTracker(string name)
        {
            pi.functionName = name;
            pi.calledAt = DateTime.Now;
            sw.Start();
        }

        public void Stop()
        {
            Task.Run(() =>
            {
                sw.Stop();
                pi.runTime = sw.ElapsedMilliseconds;
                GpsExploreContext db = new GpsExploreContext();
                db.PerformanceInfo.Add(pi);
                db.SaveChanges();
            });
            return;
        }
    }
}
