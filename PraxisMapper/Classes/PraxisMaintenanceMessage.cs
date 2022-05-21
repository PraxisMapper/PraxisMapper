
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Linq;
using System.Threading.Tasks;

namespace PraxisMapper.Classes;

public class PraxisMaintenanceMessage
{
    //Define target endpoints to protect, so webview apps will load without issues.
    public static string outputMessage;
    private readonly RequestDelegate _next;

    public PraxisMaintenanceMessage(RequestDelegate next)
    {
        _next = next;
    }

    public async Task Invoke(HttpContext context)
    {
        //consider allowing localhost connections only to skip this message, so admins could test results while external users could not.
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