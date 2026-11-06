using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PubSoft.Security;

namespace PubSoft.AspNetCore;


public sealed class PubSoftRateLimitPolicyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IReadOnlyList<PubSoftRateLimitPolicy> _policies;
    private int _proxyWarningEmitted;

    public PubSoftRateLimitPolicyMiddleware(
        RequestDelegate next,
        IEnumerable<PubSoftRateLimitPolicy> policies)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        ArgumentNullException.ThrowIfNull(policies);
        _policies = policies.ToArray();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        WarnOnUnprocessedForwardedHeaders(context);
        var remaining = int.MaxValue;
        DateTime? resetTime = null;
        var failureCounters = new List<(PubSoftRateLimitPolicy Policy, string? Identifier)>();

        foreach (var policy in _policies)
        {
            if (!policy.Applies(context))
                continue;

            var identifier = policy.GetIdentifier(context);
            var result = policy.Check(identifier, countRequest: !policy.CountFailuresOnly);
            if (!result.IsAllowed)
            {
                await WriteRateLimitResponseAsync(context, result.ResetTime).ConfigureAwait(false);
                return;
            }

            if (result.RemainingRequests < remaining)
            {
                remaining = result.RemainingRequests;
                resetTime = result.ResetTime;
            }

            if (policy.CountFailuresOnly)
                failureCounters.Add((policy, identifier));
        }

        if (remaining != int.MaxValue && resetTime.HasValue)
        {
            context.Response.Headers["RateLimit-Remaining"] = remaining.ToString(System.Globalization.CultureInfo.InvariantCulture);
            context.Response.Headers["RateLimit-Reset"] = GetSecondsUntilReset(resetTime.Value).ToString(CultureInfo.InvariantCulture);
        }

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch
        {
            RecordFailures(context, failureCounters);
            throw;
        }

        RecordFailures(context, failureCounters, context.Response.StatusCode);
    }

    private void WarnOnUnprocessedForwardedHeaders(HttpContext context)
    {
        if (Volatile.Read(ref _proxyWarningEmitted) != 0
            || !ProxyDeploymentDiagnostics.HasUnprocessedForwardedHeaders(context))
            return;
        if (Interlocked.CompareExchange(ref _proxyWarningEmitted, 1, 0) != 0)
            return;
        context.RequestServices?.GetService<ILogger<PubSoftRateLimitPolicyMiddleware>>()?
            .LogWarning(
                "Request carries X-Forwarded-For/X-Real-IP but RemoteIpAddress is the proxy address ({RemoteIp}) — forwarded headers were never processed. " +
                "All clients share one rate-limit bucket. Insert app.UsePubSoftForwardedHeaders() early in the pipeline " +
                "(X-Real-IP-only proxies also need options.RealIp = true).",
                context.Connection.RemoteIpAddress);
    }

    private static void RecordFailures(
        HttpContext context,
        IEnumerable<(PubSoftRateLimitPolicy Policy, string? Identifier)> failureCounters,
        int? statusCode = null)
    {
        foreach (var (policy, identifier) in failureCounters)
        {
            if (statusCode is null || policy.IsFailure(context, statusCode.Value))
                _ = policy.Check(identifier, countRequest: true);
        }
    }

    private static async Task WriteRateLimitResponseAsync(HttpContext context, DateTime resetTime)
    {
        var retryAfter = Math.Max(1, GetSecondsUntilReset(resetTime));
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var rejection = context.RequestServices?.GetService<PubSoftRateLimitRejection>();
        if (rejection?.BodyWriter is { } writer)
        {
            await writer(context).ConfigureAwait(false);
            return;
        }
        await context.Response.WriteAsJsonAsync(
            new { error = "rate_limit_exceeded" },
            context.RequestAborted).ConfigureAwait(false);
    }

    private static int GetSecondsUntilReset(DateTime resetTime)
        => Math.Max(0, (int)Math.Ceiling((resetTime - TimeProvider.System.GetUtcNow().UtcDateTime).TotalSeconds));
}
