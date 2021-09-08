
namespace PraxisMapper.Classes;
public class PraxisHeaderCheck
{
    private readonly RequestDelegate _next;

    //Define target endpoints to protect, so webview apps will load without issues.
    static string[] protectedControllers = new string[] { "admin", "data" }; //maptile removed for now, since Leaflet requires extra logic to handle adding that to a request url.
    public static string ServerAuthKey = "";
    public static bool enableAuthCheck = false;

    public PraxisHeaderCheck(RequestDelegate next)
    {
        this._next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        
        Microsoft.Extensions.Primitives.StringValues requestAuthKey = "";
        if (enableAuthCheck && protectedControllers.Any(c => context.Request.Path.Value.ToLower().Contains(c)) && (!context.Request.Headers.TryGetValue("PraxisAuthKey", out requestAuthKey) || ServerAuthKey != requestAuthKey))
        {
            context.Abort();
        }

        await this._next.Invoke(context).ConfigureAwait(false);
    }
}

public static class PraxisHeaderCheckExtensions
{
    public static IApplicationBuilder UsePraxisHeaderCheck(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<PraxisHeaderCheck>();
    }
}