using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PubSoft.Security;

namespace PubSoft.AspNetCore;

/// <summary>Fixed-window rate-limiting middleware partitioned by the configured identifier selector.</summary>
public sealed class PubSoftRateLimitingMiddleware
{
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private readonly RequestDelegate _next;
    private readonly RateLimiter _rateLimiter;
    private int _proxyWarningEmitted;

    /// <summary>Creates the middleware.</summary>
    public PubSoftRateLimitingMiddleware(RequestDelegate next, RateLimiter rateLimiter)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
    }

    /// <summary>Applies the rate limit then forwards.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        WarnOnUnprocessedForwardedHeaders(context);
        var ipAddress = IpNormalizer.Normalize(context.Connection.RemoteIpAddress?.ToString());
        var result = _rateLimiter.CheckByIpAddress(ipAddress);
        if (result.RemainingRequests != int.MaxValue)
        {
            context.Response.Headers["RateLimit-Remaining"] = result.RemainingRequests.ToString(System.Globalization.CultureInfo.InvariantCulture);
            context.Response.Headers["RateLimit-Reset"] = GetSecondsUntilReset(result.ResetTime).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (!result.IsAllowed)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = Math.Max(1, GetSecondsUntilReset(result.ResetTime))
                .ToString(System.Globalization.CultureInfo.InvariantCulture);
            var rejection = context.RequestServices?.GetService<PubSoftRateLimitRejection>();
            if (rejection?.BodyWriter is { } writer)
            {
                await writer(context).ConfigureAwait(false);
                return;
            }
            await context.Response.WriteAsJsonAsync(
                new { error = "rate_limit_exceeded" },
                context.RequestAborted).ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);
    }

    private void WarnOnUnprocessedForwardedHeaders(HttpContext context)
    {
        if (Volatile.Read(ref _proxyWarningEmitted) != 0
            || !ProxyDeploymentDiagnostics.HasUnprocessedForwardedHeaders(context))
            return;
        if (Interlocked.CompareExchange(ref _proxyWarningEmitted, 1, 0) != 0)
            return;
        context.RequestServices?.GetService<ILogger<PubSoftRateLimitingMiddleware>>()?
            .LogWarning(
                "Request carries X-Forwarded-For/X-Real-IP but RemoteIpAddress is the proxy address ({RemoteIp}) - forwarded headers were never processed. " +
                "All clients share one rate-limit bucket. Insert app.UsePubSoftForwardedHeaders() early in the pipeline " +
                "(X-Real-IP-only proxies also need options.RealIp = true).",
                context.Connection.RemoteIpAddress);
    }

    private int GetSecondsUntilReset(DateTime resetTime)
        => Math.Max(0, (int)Math.Ceiling((resetTime - _timeProvider.GetUtcNow().UtcDateTime).TotalSeconds));
}
