namespace PraxisMapper.Classes
{
    public class ErrorHandlerMiddleware
    {
        private readonly RequestDelegate _next;

        public ErrorHandlerMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception error)
            {
                ErrorLogger.LogError(error);
                var response = context.Response;
                response.ContentType = "text/text";
                response.StatusCode = 500;


                var result = "Server error";
                await response.WriteAsync(result);
            }
        }
    }

    public static class PraxisErrorExtensions
    {
        public static IApplicationBuilder UseGlobalErrorHandler(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<ErrorHandlerMiddleware>();
        }
    }
}