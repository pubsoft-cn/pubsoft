using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PubSoft.Core;

namespace PubSoft.AspNetCore;

public sealed class ExceptionHandlingMiddleware
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (context.Response.HasStarted)
            {
                _logger.LogError(exception,
                    "Unhandled exception after response started. CorrelationId: {CorrelationId}",
                    context.TraceIdentifier);
                throw;
            }

            await WriteErrorResponseAsync(context, exception).ConfigureAwait(false);
        }
    }

    private async Task WriteErrorResponseAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.TraceIdentifier;
        var isDevelopment = _environment.IsDevelopment();
        var (statusCode, errorCode, message) = exception switch
        {
            DomainException domainException =>
                (domainException.StatusCode is >= 400 and <= 499
                    ? (HttpStatusCode)domainException.StatusCode
                    : HttpStatusCode.BadRequest, "domain_error", domainException.Message),
            ArgumentException =>
                (HttpStatusCode.BadRequest, "invalid_argument", "Invalid request."),
            UnauthorizedAccessException =>
                (HttpStatusCode.Unauthorized, "unauthorized", "Authentication required."),
            _ =>
                (HttpStatusCode.InternalServerError, "internal_error",
                    isDevelopment ? exception.Message : "An internal server error occurred.")
        };

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            _logger.LogError(exception, "Unhandled exception. CorrelationId: {CorrelationId}", correlationId);
        }
        else
        {
            _logger.LogWarning(exception, "Handled exception. CorrelationId: {CorrelationId}", correlationId);
        }

        context.Response.Clear();
        context.Response.StatusCode = (int)statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        var payload = new ErrorResponse(
            Ok: false,
            Error: errorCode,
            Message: message,
            CorrelationId: correlationId,
            StackTrace: isDevelopment && statusCode == HttpStatusCode.InternalServerError
                ? exception.StackTrace
                : null);
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            payload,
            JsonOptions,
            context.RequestAborted).ConfigureAwait(false);
    }

    private sealed record ErrorResponse(
        bool Ok,
        string Error,
        string Message,
        string CorrelationId,
        string? StackTrace);
}

public static class ExceptionHandlingMiddlewareExtensions
{
    public static IApplicationBuilder UsePubSoftExceptionHandling(this IApplicationBuilder app)
        => app.UseMiddleware<ExceptionHandlingMiddleware>();
}