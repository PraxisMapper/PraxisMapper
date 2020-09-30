using DatabaseAccess;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static DatabaseAccess.DbTables;

namespace PerformanceTestApp
{
    public class PerformanceTracker
    {
        //A slightly trimmed down version of the class from the server, to test DB insert speed.
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
            sw.Stop();
            pi.runTime = sw.ElapsedMilliseconds;
            GpsExploreContext db = new GpsExploreContext();
            db.PerformanceInfo.Add(pi);
            db.SaveChanges();
            return;
        }

        public void StopNoChangeTracking()
        {
            sw.Stop();
            pi.runTime = sw.ElapsedMilliseconds;
            GpsExploreContext db = new GpsExploreContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            db.PerformanceInfo.Add(pi);
            db.SaveChanges();
            return;
        }

        public void StopSproc()
        {
            sw.Stop();
            GpsExploreContext db = new GpsExploreContext();
            db.Database.ExecuteSqlRaw("SavePerfInfo @p0, @p1, @p2, @p3", parameters: new object[] { pi.functionName, sw.ElapsedMilliseconds, pi.calledAt, "" });
            return;
        }
    }
}