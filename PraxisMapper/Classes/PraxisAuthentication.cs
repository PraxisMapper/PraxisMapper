﻿using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using PraxisCore;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PraxisMapper.Classes
{
    /// <summary>
    /// Handles authentication, allowing paths through without authentication, and getting the auth info on the user making a request.
    /// </summary>
    public class PraxisAuthentication {
        private readonly RequestDelegate _next;
        private static ConcurrentDictionary<string, AuthData> authTokens = new ConcurrentDictionary<string, AuthData>(); //string is authtoken (Guid), AuthData contains username.
        public static ConcurrentBag<string> whitelistedPaths = new ConcurrentBag<string>();
        public static HashSet<string> admins = new HashSet<string>();
        public PraxisAuthentication(RequestDelegate next) {
            this._next = next;
        }

        public async Task Invoke(HttpContext context) { 
            var key = context.Request.Headers.FirstOrDefault(h => h.Key == "AuthKey");
            bool loggedIn = false;
            AuthData data = null;
            if (key.Value.Count > 0) {
                loggedIn = authTokens.TryGetValue(key.Value, out data);

                if (loggedIn)
                {
                    context.Response.Headers.Append("X-account", data.accountId);
                    context.Response.Headers.Append("X-internalPwd", data.intPassword);
                }

                context.Response.OnStarting(() => {
                    context.Response.Headers.Remove("X-account");
                    context.Response.Headers.Remove("X-internalPwd");
                    return Task.CompletedTask;
                });

                if (data != null && data.expiration < DateTime.UtcNow) {
                    authTokens.TryRemove(key.Value, out _);
                    context.Response.StatusCode = StatusCodes.Status419AuthenticationTimeout;
                    return;
                }
            }

            var path = context.Request.Path.Value;
            if (data != null && data.isGdprRequest && !path.Contains("gdpr", StringComparison.OrdinalIgnoreCase))
                return;

            if (!whitelistedPaths.Any(p => path.Contains(p))) {

                if (key.Key == null || !loggedIn) {
                    if (Startup.Configuration.GetValue<bool>("enablePerformanceTracker")) {
                        PraxisPerformanceTracker.LogInfoToPerfData("PraxisAuth", "Auth Failed for " + context.Request.Path);
                    }

                    System.Threading.Thread.Sleep(2000); //A mild annoyance to anyone attempting to brute-force a key from the outside
                    context.Abort();
                    return;
                }
            }

            await this._next.Invoke(context).ConfigureAwait(false);
        }
        public static bool AddEntry(AuthData entry) {
            return authTokens.TryAdd(entry.authToken, entry);
        }
        public static bool RemoveEntry(string authToken) {
            return authTokens.TryRemove(authToken, out _);
        }

        public static void DropExpiredEntries() {
            List<string> toRemoveAuth = new List<string>();
            foreach (var d in authTokens) {
                if (d.Value.expiration < DateTime.UtcNow)
                    toRemoveAuth.Add(d.Key);
            }
            foreach (var d in toRemoveAuth)
                authTokens.TryRemove(d, out _);
        }

        public static void GetAuthInfo(HttpResponse response, out string account, out string password) {
            account = "";
            password = "";
            if (response.Headers.TryGetValue("X-account", out var xAccount))
                account = xAccount;
            if (response.Headers.TryGetValue("X-internalPwd", out var xIntPwd))
                password = xIntPwd;
        }

        public static bool IsAdmin(string accountId) {
            return admins.Contains(accountId);
        }
    }

    public static class PraxisAuthenticationExtensions {
        /// <summary>
        /// Enables authentication against the PraxisMapper database.
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static IApplicationBuilder UsePraxisAuthentication(this IApplicationBuilder builder) {
            return builder.UseMiddleware<PraxisAuthentication>();
        }
    }
}
