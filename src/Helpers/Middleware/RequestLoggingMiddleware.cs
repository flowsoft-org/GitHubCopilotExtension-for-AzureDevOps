using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

public class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_logger.IsEnabled(LogLevel.Debug)) {
            // Log headers
            _logger.LogDebug("Headers: {Headers}", context.Request.Headers);

            // Log query string
            _logger.LogDebug("Query String: {QueryString}", context.Request.QueryString);

            // Log body (if applicable)
            if (context.Request.ContentLength > 0)
            {
                context.Request.EnableBuffering(); // Allow reading the body multiple times
                var copyBodyStream = new MemoryStream();
                await context.Request.Body.CopyToAsync(copyBodyStream);
                context.Request.Body.Position = 0;
                copyBodyStream.Position = 0;
                using var reader = new StreamReader(copyBodyStream);
                var body = await reader.ReadToEndAsync();
                _logger.LogDebug("Body: {Body}", body);
            }
        }
        // Call the next middleware in the pipeline
        await _next(context);
    }
}