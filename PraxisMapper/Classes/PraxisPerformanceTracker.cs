using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using PraxisCore;
using System;
using System.Threading.Tasks;

namespace PraxisMapper.Classes
{
    public class PraxisPerformanceTracker
    {
        private readonly RequestDelegate _next;
        public static bool Enabled = false;
        public PraxisPerformanceTracker(RequestDelegate next)
        {
            this._next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            if (Enabled && !context.Response.Headers.ContainsKey("X-noPerfTrack"))
            {
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                var pi = new DbTables.PerformanceInfo();
                pi.FunctionName = context.Request.Path;
                pi.CalledAt = DateTime.UtcNow;
                context.Response.OnStarting(() => {
                    sw.Stop();
                    pi.RunTime = sw.ElapsedMilliseconds;
                    if (context.Response.Headers.ContainsKey("X-notes"))
                        pi.Notes = context.Response.Headers["X-notes"];
                    PraxisContext db = new PraxisContext();
                    db.ChangeTracker.AutoDetectChangesEnabled = false;
                    db.PerformanceInfo.Add(pi);
                    db.SaveChanges();
                    return Task.CompletedTask;
                });
            }
            await this._next.Invoke(context).ConfigureAwait(false);
        }
    }

    public static class PraxisPerformanceTrackerExtensions
    {
        public static IApplicationBuilder UsePraxisPerformanceTracker(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<PraxisPerformanceTracker>();
        }
    }
}
