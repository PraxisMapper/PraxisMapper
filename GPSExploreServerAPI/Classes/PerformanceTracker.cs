using DatabaseAccess;
using static DatabaseAccess.DbTables;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics.Eventing.Reader;
using Microsoft.EntityFrameworkCore;

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
            Stop("");
        }

        public void Stop(string notes)
        {
            if (!EnableLogging) return;
            sw.Stop();
            pi.runTime = sw.ElapsedMilliseconds;
            pi.notes = notes;
            GpsExploreContext db = new GpsExploreContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false; //Diabling this saves ~17ms per call, which can be important on the webserver. Sproc is trivially faster than that.
            db.PerformanceInfo.Add(pi);
            db.SaveChanges();
            return;
        }
    }
}
