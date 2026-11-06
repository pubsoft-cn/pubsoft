using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PubSoft.Core;

namespace PubSoft.AspNetCore;

public sealed class GlobalExceptionHandlerOptions
{
    public int DomainExceptionStatusCode { get; set; } = 400;
    public int ArgumentExceptionStatusCode { get; set; } = 400;
    public int InvalidOperationExceptionStatusCode { get; set; } = 400;
    public int UnauthorizedAccessStatusCode { get; set; } = 401;
    public int KeyNotFoundStatusCode { get; set; } = 404;
    public int DefaultStatusCode { get; set; } = 500;
    public string DefaultMessage { get; set; } = "server error";
    public string UnauthorizedMessage { get; set; } = "unauthorized";
    public Dictionary<Type, int> CustomMappings { get; } = new();
}

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly GlobalExceptionHandlerOptions _options;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, GlobalExceptionHandlerOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new();
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, message) = Resolve(exception);
        if (statusCode >= 500)
            _logger.LogError(exception, "Unhandled exception: {Message}", exception.Message);

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/json; charset=utf-8";
        await httpContext.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            code = statusCode,
            message,
            traceId = httpContext.TraceIdentifier,
        }), cancellationToken).ConfigureAwait(false);
        return true;
    }

    private (int StatusCode, string Message) Resolve(Exception exception)
    {
        foreach (var (type, code) in _options.CustomMappings)
            if (type.IsInstanceOfType(exception))
                return (code, exception.Message);
        return exception switch
        {
            DomainException => (_options.DomainExceptionStatusCode, exception.Message),
            ArgumentException => (_options.ArgumentExceptionStatusCode, exception.Message),
            InvalidOperationException => (_options.InvalidOperationExceptionStatusCode, exception.Message),
            UnauthorizedAccessException => (_options.UnauthorizedAccessStatusCode, _options.UnauthorizedMessage),
            KeyNotFoundException => (_options.KeyNotFoundStatusCode, exception.Message),
            _ => (_options.DefaultStatusCode, _options.DefaultMessage),
        };
    }
}
