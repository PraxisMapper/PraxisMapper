
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Linq;
using System.Threading.Tasks;

namespace PraxisMapper.Classes;
//NOTE: PraxiHeaderCheck is intended as a simple app guard, that doesn't require logging in. 
//If you have an authentication system in place, or use the PraxisAuthentication middleware,
//this probably does no additional help.
public class PraxisMaintenanceMessage
{
    //Define target endpoints to protect, so webview apps will load without issues.
    public static string outputMessage;

    public PraxisMaintenanceMessage(RequestDelegate next)
    {
    }

    public async Task Invoke(HttpContext context)
    {
        var response = context.Response;
        response.ContentType = "text/text";
        response.StatusCode = 500;
        await response.WriteAsync(outputMessage);
    }
}

public static class PraxisMaintenanceMessageExtensions
{
    public static IApplicationBuilder UsePraxisMaintenanceMessage(this IApplicationBuilder builder, string message)
    {
        PraxisMaintenanceMessage.outputMessage = message;
        return builder.UseMiddleware<PraxisMaintenanceMessage>();
    }
}