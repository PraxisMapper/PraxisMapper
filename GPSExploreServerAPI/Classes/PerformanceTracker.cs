using DatabaseAccess;
using static DatabaseAccess.DbTables;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics.Eventing.Reader;

namespace GPSExploreServerAPI.Classes
{
    public class PerformanceTracker
    {
        //TODO: add toggle for this somewhere in the server config.
        public static bool EnableLogging = true;

        PerformanceInfo pi = new PerformanceInfo();
        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        public PerformanceTracker(string name)
        {
            if (!EnableLogging) return;
            pi.functionName = name;
            pi.calledAt = DateTime.Now;
            sw.Start();
        }

        public void Stop()
        {
            if (!EnableLogging) return;
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
            if (!EnableLogging) return;
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
