using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using PraxisCore;
using System;
using System.Threading.Tasks;

namespace PraxisMapper.Classes {
    public class PraxisPerformanceTracker {
        private readonly RequestDelegate _next;
        public PraxisPerformanceTracker(RequestDelegate next) {
            this._next = next;
        }

        public async Task Invoke(HttpContext context) {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            var pi = new DbTables.PerformanceInfo();
            pi.FunctionName = context.Request.Path;
            pi.CalledAt = DateTime.UtcNow;
            context.Response.OnStarting(() => {
                sw.Stop();
                pi.RunTime = sw.ElapsedMilliseconds;
                if (context.Response.Headers.TryGetValue("X-notes", out var notes))
                    pi.Notes = notes;
                if (context.Response.Headers.TryGetValue("X-noPerfTrack", out var sanitized))
                    pi.FunctionName = sanitized;
                PraxisContext db = new PraxisContext();
                db.ChangeTracker.AutoDetectChangesEnabled = false;
                db.PerformanceInfo.Add(pi);
                db.SaveChanges();
                return Task.CompletedTask;
            });

            await this._next.Invoke(context).ConfigureAwait(false);
        }

        /// <summary>
        /// Insert a note into the PerformanceData table for reference later.
        /// </summary>
        /// <param name="functionName"></param>
        /// <param name="notes"></param>
        public static void LogInfoToPerfData(string functionName, string notes) {
            var pi = new DbTables.PerformanceInfo();
            pi.FunctionName = functionName;
            pi.Notes = notes;
            pi.CalledAt = DateTime.UtcNow;
            PraxisContext db = new PraxisContext();
            db.ChangeTracker.AutoDetectChangesEnabled = false;
            db.PerformanceInfo.Add(pi);
            db.SaveChanges();
        }
    }

    public static class PraxisPerformanceTrackerExtensions {
        //Enable performance tracking to see how long all calls take to run on the server.
        public static IApplicationBuilder UsePraxisPerformanceTracker(this IApplicationBuilder builder) {
            return builder.UseMiddleware<PraxisPerformanceTracker>();
        }
    }
}