using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PubSoft.Core;

namespace PubSoft.AspNetCore;


/// <summary><see cref="IExceptionHandler"/> mapping exceptions to status-coded JSON responses.</summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly GlobalExceptionHandlerOptions _options;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    /// <summary>Creates the handler.</summary>
    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, GlobalExceptionHandlerOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new();
    }

    /// <inheritdoc />
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
