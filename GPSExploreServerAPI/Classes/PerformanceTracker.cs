using DatabaseAccess;
using static DatabaseAccess.DbTables;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace GPSExploreServerAPI.Classes
{
    public class PerformanceTracker
    {
        PerformanceInfo pi = new PerformanceInfo();
        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        public PerformanceTracker(string name)
        {
            pi.functionName = name;
            pi.calledAt = DateTime.Now;
            sw.Start();
        }

        public void Stop()
        {
            //Task.Run(() =>
            //{
                sw.Stop();
                pi.runTime = sw.ElapsedMilliseconds;
                GpsExploreContext db = new GpsExploreContext();
                db.PerformanceInfo.Add(pi);
                db.SaveChanges();
            //});
            return;
        }

        public void Stop(string notes)
        {
            //Task.Run(() =>
            //{
                sw.Stop();
                pi.runTime = sw.ElapsedMilliseconds;
                pi.notes = notes;
                GpsExploreContext db = new GpsExploreContext();
                db.PerformanceInfo.Add(pi);
                db.SaveChanges();
            //});
            return;
        }
    }
}
