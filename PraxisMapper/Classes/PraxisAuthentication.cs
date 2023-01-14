using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Identity.Client;
using PraxisCore;
using RTools_NTS.Util;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PraxisMapper.Classes
{
    public class PraxisAuthentication
    {
        private readonly RequestDelegate _next;
        private static ConcurrentDictionary<string, AuthData> authTokens = new ConcurrentDictionary<string, AuthData>(); //string is authtoken (Guid)
        public static ConcurrentBag<string> whitelistedPaths = new ConcurrentBag<string>();
        public static HashSet<string> admins = new HashSet<string>();
        public PraxisAuthentication(RequestDelegate next)
        {
            this._next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            AuthData data;
            var key = context.Request.Headers.FirstOrDefault(h => h.Key == "AuthKey");
            if (key.Value.Count > 0)
            {
                data = authTokens[key.Value];

                context.Response.Headers.Add("X-account", data.accountId);
                context.Response.Headers.Add("X-internalPwd", data.intPassword);

                context.Response.OnStarting(() =>
                {
                    context.Response.Headers.Remove("X-account");
                    context.Response.Headers.Remove("X-internalPwd");
                    return Task.CompletedTask;
                });

                if (data.expiration < DateTime.UtcNow)
                {
                    authTokens.TryRemove(key.Value, out var ignore);
                    context.Response.StatusCode = StatusCodes.Status419AuthenticationTimeout;
                    return;
                }
            }

            var path = context.Request.Path.Value;
            if (!whitelistedPaths.Any(p => path.Contains(p)))
            {
                
                if (key.Key == null || !authTokens.ContainsKey(key.Value))
                {
                    if (PraxisMapper.Startup.Configuration.GetValue<bool>("enablePerformanceTracker"))
                    {
                        var db = new PraxisContext();
                        db.PerformanceInfo.Add(new DbTables.PerformanceInfo() { Notes = "Auth Failed for " + context.Request.Path, FunctionName = "PraxisAuth", CalledAt = DateTime.UtcNow });
                        db.SaveChangesAsync();
                    }

                    context.Abort();
                    return;
                }
            }

            await this._next.Invoke(context).ConfigureAwait(false);
        }
        public static bool AddEntry(AuthData entry)
        {
            return authTokens.TryAdd(entry.authToken, entry);
        }
        public static bool RemoveEntry(string accountId)
        {
            return authTokens.TryRemove(accountId, out var ignore);
        }

        public static void DropExpiredEntries()
        {
            List<string> toRemoveAuth = new List<string>();
            foreach (var d in authTokens)
            {
                if (d.Value.expiration < DateTime.UtcNow)
                    toRemoveAuth.Add(d.Key);
            }
            foreach (var d in toRemoveAuth)
                authTokens.TryRemove(d, out var ignore);
        }

        public static void GetAuthInfo(HttpResponse response, out string account, out string password)
        {
            account = "";
            password = "";
            if (response.Headers.ContainsKey("X-account"))
                account = response.Headers["X-account"].ToString();
            if (response.Headers.ContainsKey("X-internalPwd"))
                password = response.Headers["X-internalPwd"].ToString();
        }

        public static bool IsAdmin(string accountId)
        {
            return admins.Contains(accountId);
        }
    }

    public static class PraxisAuthenticationExtensions
    {
        public static IApplicationBuilder UsePraxisAuthentication(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<PraxisAuthentication>();
        }
    }
}
