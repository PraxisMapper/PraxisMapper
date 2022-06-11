using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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
        public static ConcurrentDictionary<string, AuthData> authTokens = new ConcurrentDictionary<string, AuthData>();
        public static ConcurrentBag<string> whitelistedPaths = new ConcurrentBag<string>(); 
        //TODO: How would a plugin get this whitelist exposed to add to if it wanted? It probaly wont, but if it did.
        public PraxisAuthentication(RequestDelegate next)
        {
            this._next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            //TODO: allow any request from localhost? or make Slippy login first?
            var path = context.Request.Path.Value;
            if (!whitelistedPaths.Any(p => path.Contains(p)))
            {
                var ch = context.Request.Headers;
                if (!ch.Any(h => h.Key == "AuthKey"))
                    context.Abort();

                var key = ch.First(h => h.Key == "AuthKey").Value;
                var account = ch.First(h => h.Key == "Account").Value;
                var data = authTokens[key];
                if (data.accountId != account)
                    context.Abort();

                if (data.expiration < DateTime.UtcNow)
                {
                    authTokens.TryRemove(key, out var ignore);
                    context.Response.StatusCode = StatusCodes.Status419AuthenticationTimeout;
                    return;
                }
            }

            await this._next.Invoke(context).ConfigureAwait(false);
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
