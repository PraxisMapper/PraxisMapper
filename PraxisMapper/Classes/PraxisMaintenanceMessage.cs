using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Threading.Tasks;

namespace PraxisMapper.Classes;

//If a maintenance message is set, non-admins will only get a 500 response with that message. Admins will get passed into the server.
public class PraxisMaintenanceMessage {
    public static string outputMessage;
    private readonly RequestDelegate _next;

    public PraxisMaintenanceMessage(RequestDelegate next) {
        _next = next;
    }

    public async Task Invoke(HttpContext context) {
        if (outputMessage != "") {
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
    public static IApplicationBuilder UsePraxisMaintenanceMessage(this IApplicationBuilder builder, string message) {
        PraxisMaintenanceMessage.outputMessage = message;
        return builder.UseMiddleware<PraxisMaintenanceMessage>();
    }
}