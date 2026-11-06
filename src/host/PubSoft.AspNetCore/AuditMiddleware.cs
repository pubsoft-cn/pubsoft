using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PubSoft.Auditing;

namespace PubSoft.AspNetCore;

public sealed class AuditMiddlewareOptions
{
    public bool CaptureClientIp { get; set; }
    public bool CaptureUserAgent { get; set; }
    public bool CaptureActor { get; set; }
}

public sealed class AuditMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AuditMiddlewareOptions _options;

    public AuditMiddleware(RequestDelegate next, AuditMiddlewareOptions? options = null)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _options = options ?? new AuditMiddlewareOptions();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var scope = services.GetService<IAuditScope>();
        var sink = services.GetService<IAuditSnapshotSink>();
        if (scope is null || sink is null)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        scope.Initialize(new AuditRequestContext(
            TimeProvider.System.GetUtcNow(),
            context.Request.Method,
            context.Request.Path.Value,
            _options.CaptureClientIp && context.Connection.RemoteIpAddress is { } remoteIp
                ? IpNormalizer.Normalize(remoteIp.ToString())
                : null,
            _options.CaptureUserAgent ? context.Request.Headers.UserAgent.ToString() : null,
            GetActor(services)));

        var success = false;
        try
        {
            await _next(context).ConfigureAwait(false);
            success = context.Response.StatusCode < StatusCodes.Status400BadRequest;
        }
        catch
        {
            if (scope.IsEnabled)
                scope.MarkFailure();
            throw;
        }
        finally
        {
            try
            {
                var snapshot = scope.Finalize(success, context.Response.StatusCode);
                if (snapshot is not null)
                    await sink.WriteAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                services.GetService<ILogger<AuditMiddleware>>()?.LogWarning(
                    "Audit finalization failed ({ExceptionType}).",
                    exception.GetType().Name);
            }
        }
    }

    private AuditActorContext? GetActor(IServiceProvider services)
    {
        if (!_options.CaptureActor)
            return null;

        try
        {
            return services.GetService<IAuditActorAccessor>()?.GetCurrentActor();
        }
        catch (Exception exception)
        {
            services.GetService<ILogger<AuditMiddleware>>()?.LogWarning(
                "Audit actor resolution failed ({ExceptionType}).",
                exception.GetType().Name);
            return null;
        }
    }
}

public static class AuditMiddlewareExtensions
{
    public static IApplicationBuilder UsePubSoftAudit(
        this IApplicationBuilder app,
        AuditMiddlewareOptions? options = null)
        => app.UseMiddleware<AuditMiddleware>(options);
}