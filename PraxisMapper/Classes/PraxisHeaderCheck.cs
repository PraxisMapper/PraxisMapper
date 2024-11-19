
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Linq;
using System.Threading.Tasks;

namespace PraxisMapper.Classes;
/// <summary>
/// PraxiHeaderCheck is intended as a simple app guard, that doesn't require logging in. If you have an authentication system in place, 
/// or use the PraxisAuthentication middleware, this probably does no additional help. If enabled, makes sure that requests have a header PraxisAuthKey equal to ServerAuthKey in the configuration.
/// </summary>
public class PraxisHeaderCheck {
    private readonly RequestDelegate _next;
    //Define target endpoints to protect, so webview apps will load without issues.
    //NOTE: header check only blocks endpoints in the core server. Plugin controller paths aren't protected by it!
    static string[] protectedControllers = new string[] { "admin", "data", "maptile", "securedata", "server", "styledata" };
    public static string ServerAuthKey = "";

    public PraxisHeaderCheck(RequestDelegate next) {
        this._next = next;
    }

    public async Task Invoke(HttpContext context) {
        if (protectedControllers.Any(c => context.Request.Path.Value.Contains(c, System.StringComparison.OrdinalIgnoreCase))
            && !(context.Request.Headers.Any(h => h.Key == "PraxisAuthKey" && h.Value == ServerAuthKey)
            || context.Request.Query.Any(q => q.Key.ToLower() == "praxisauthkey" && q.Value == ServerAuthKey))) {
            context.Abort();
            return;
        }

        await this._next.Invoke(context).ConfigureAwait(false);
    }
}

public static class PraxisHeaderCheckExtensions {
    public static IApplicationBuilder UsePraxisHeaderCheck(this IApplicationBuilder builder) {
        return builder.UseMiddleware<PraxisHeaderCheck>();
    }
}