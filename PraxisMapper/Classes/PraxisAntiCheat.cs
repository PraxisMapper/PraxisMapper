using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PraxisMapper.Classes
{
    public class AntiCheatInfo
    {
        public List<string> entriesValidated = new List<string>();
        public DateTime validUntil = DateTime.Now;
        public DateTime lastValidated = DateTime.Now;
    }

    public class PraxisAntiCheat
    {
        private readonly RequestDelegate _next;
        public static ConcurrentDictionary<string, AntiCheatInfo> antiCheatStatus = new ConcurrentDictionary<string, AntiCheatInfo>();
        public static long expectedCount = 0;

        public PraxisAntiCheat(RequestDelegate next)
        {
            this._next = next;
        }

        public async Task Invoke(HttpContext context)
        {

            if (!context.Request.Path.Value.ToLower().Contains("anticheat")) //TODO finalize paths.
            {
                //do anti-cheat lookup.
                //TODO: correct logic
                //if we're still in the validated window, continue
                //if we are not valid, send a status code to indicate to the client it needs to re-do AntiCheat. Pick a specific number. (511 is closest I'll get on official codes)
                //NOTE: current logic means that redoing AntiCheat only needs to check 1 file if you've been playing nonstop over the expiration timer.
                if (antiCheatStatus.TryGetValue(context.Connection.RemoteIpAddress.ToString(), out AntiCheatInfo status))
                {
                    if (status.validUntil > DateTime.Now)
                    {
                        await this._next.Invoke(context).ConfigureAwait(false);
                    }
                    else
                    {
                        context.Response.StatusCode = 511; //magic number we'll use to tell the client to run anti-cheat.
                        context.Abort();
                        return;
                    }
                }
                else
                {
                    PraxisAntiCheat.antiCheatStatus.TryAdd(context.Connection.RemoteIpAddress.ToString(), new AntiCheatInfo());
                    context.Response.StatusCode = 511; //magic number we'll use to tell the client to run anti-cheat.
                    context.Abort();
                    return;
                }
            }

            await this._next.Invoke(context).ConfigureAwait(false); //allow anticheat checks as normal so clients can validate in.
        }
    }

    public static class PraxisAntiCheatExtensions
    {
        public static IApplicationBuilder UsePraxisAntiCheat(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<PraxisAntiCheat>();
        }
    }

}