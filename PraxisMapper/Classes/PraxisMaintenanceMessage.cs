using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace PraxisMapper.Classes;

/// <summary>
/// A simple class to put a PraxisMapper server into maintenance mode. If a maintenance message is set, non-admins will only get a 500 response with that message. Admins will get passed into the server.
/// </summary>
public class PraxisMaintenanceMessage {
    public static string outputMessage;
    private readonly RequestDelegate _next;

    public PraxisMaintenanceMessage(RequestDelegate next) {
        _next = next;
    }

    public async Task Invoke(HttpContext context) {
        if (outputMessage != "" && !context.Request.Path.Value.Contains("Login")) { //need to be able to login anyways.
            PraxisAuthentication.GetAuthInfo(context.Response, out var accountId, out _);
            if (!PraxisAuthentication.IsAdmin(accountId)) {
                var response = context.Response;
                response.ContentType = "text/text";
                response.StatusCode = 500;
                await response.WriteAsync(outputMessage);
            }
        }

        await this._next.Invoke(context).ConfigureAwait(false);
    }
}

public static class PraxisMaintenanceMessageExtensions {
    /// <summary>
    /// Enables setting a maintenance message that blocks all non-admin users while allowing admins to use the server.
    /// </summary>
    /// <param name="builder"></param>
    /// <param name="message"></param>
    /// <returns></returns>
    public static IApplicationBuilder UsePraxisMaintenanceMessage(this IApplicationBuilder builder, string message) {
        PraxisMaintenanceMessage.outputMessage = message;
        return builder.UseMiddleware<PraxisMaintenanceMessage>();
    }
}