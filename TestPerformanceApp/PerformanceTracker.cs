using PraxisCore;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static PraxisCore.DbTables;

namespace PerformanceTestApp
{
    public class PerformanceTracker
    {
        //A slightly trimmed down version of the class from the server, to test DB insert speed.
        PerformanceInfo pi = new PerformanceInfo();
        System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        public PerformanceTracker(string name)
        {
            pi.FunctionName = name;
            pi.CalledAt = DateTime.UtcNow;
            sw.Start();
        }

        public void Stop()
        {
            sw.Stop();
            pi.RunTime = sw.ElapsedMilliseconds;
            PraxisContext db = new PraxisContext();
            db.PerformanceInfo.Add(pi);
            db.SaveChanges();
            return;
        }

        public void StopNoChangeTracking()
        {
            sw.Stop();
            pi.RunTime = sw.ElapsedMilliseconds;
            PraxisContext db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            db.PerformanceInfo.Add(pi);
            db.SaveChanges();
            return;
        }

        public void StopSproc()
        {
            sw.Stop();
            PraxisContext db = new PraxisContext();
            db.Database.ExecuteSqlRaw("SavePerfInfo @p0, @p1, @p2, @p3", parameters: new object[] { pi.FunctionName, sw.ElapsedMilliseconds, pi.CalledAt, "" });
            return;
        }
    }
}