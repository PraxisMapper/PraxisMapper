using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System;
using System.Threading.Tasks;

namespace PraxisMapper.Classes {
    public class ErrorHandlerMiddleware {
        private readonly RequestDelegate _next;

        public ErrorHandlerMiddleware(RequestDelegate next) {
            _next = next;
        }

        public async Task Invoke(HttpContext context) {
            try {
                await _next(context);
            }
            catch (Exception error) {
                ErrorLogger.LogError(error);
                var response = context.Response;
                response.ContentType = "text/text";
                response.StatusCode = 500;

                var result = "Server error";
                await response.WriteAsync(result);
            }
        }
    }

    public static class PraxisErrorExtensions {
        /// <summary>
        /// Enables the GlobalErrorHandler for the application. Wraps all calls in a try/catch block, writes any unhandled error to the database.
        /// </summary>
        /// <param name="builder"></param>
        /// <returns></returns>
        public static IApplicationBuilder UseGlobalErrorHandler(this IApplicationBuilder builder) {
            return builder.UseMiddleware<ErrorHandlerMiddleware>();
        }
    }
}