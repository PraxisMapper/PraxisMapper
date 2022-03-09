using PraxisCore;
using System;
using static PraxisCore.DbTables;
using System.Diagnostics;

namespace PraxisMapper.Classes
{
    public class PerformanceTracker
    {
        //A simple, short class for tracking how long each function call takes. Useful for getting an idea of function runtimes. Toggleable via EnableLogging. 
        //the amount of overhead this class adds is pretty neglible, something under 5ms per API call.
        public static bool EnableLogging = true;

        PerformanceInfo pi = new PerformanceInfo();
        Stopwatch sw = new Stopwatch();
        public PerformanceTracker(string name)
        {
            if (!EnableLogging) return;
            pi.FunctionName = name;
            pi.CalledAt = DateTime.UtcNow;
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
            pi.RunTime = sw.ElapsedMilliseconds;
            pi.Notes = notes;
            PraxisContext db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false; //Diabling this saves ~17ms per call, which can be important on the webserver. Sproc is trivially faster than this setup.
            db.PerformanceInfo.Add(pi);
            db.SaveChanges();
            return;
        }
    }
}
