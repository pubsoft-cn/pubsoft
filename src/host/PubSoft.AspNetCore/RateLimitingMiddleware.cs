using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PubSoft.Security;
using System.Globalization;

namespace PubSoft.AspNetCore;

public sealed class PubSoftRateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RateLimiter _rateLimiter;
    private int _proxyWarningEmitted;

    public PubSoftRateLimitingMiddleware(RequestDelegate next, RateLimiter rateLimiter)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
    }

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
                "Request carries X-Forwarded-For/X-Real-IP but RemoteIpAddress is the proxy address ({RemoteIp}) — forwarded headers were never processed. " +
                "All clients share one rate-limit bucket. Insert app.UsePubSoftForwardedHeaders() early in the pipeline " +
                "(X-Real-IP-only proxies also need options.RealIp = true).",
                context.Connection.RemoteIpAddress);
    }

    private static int GetSecondsUntilReset(DateTime resetTime)
        => Math.Max(0, (int)Math.Ceiling((resetTime - DateTime.UtcNow).TotalSeconds));
}

public static class PubSoftRateLimitingExtensions
{
    public static IServiceCollection AddPubSoftRateLimiting(
        this IServiceCollection services,
        Action<RateLimiterOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new RateLimiterOptions();
        configure?.Invoke(options);
        if (options.LimitByUserId)
            throw new ArgumentException("The ASP.NET rate-limiting adapter currently supports remote IP addresses only.", nameof(configure));

        var rateLimiter = new RateLimiter(options);
        services.TryAddSingleton(rateLimiter);
        return services;
    }

    public static IApplicationBuilder UsePubSoftRateLimiting(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<PubSoftRateLimitingMiddleware>();
    }
}

public sealed class PubSoftRateLimitPolicyOptions
{
    public string Name { get; set; } = string.Empty;
    public int RequestsPerWindow { get; set; } = 100;
    public TimeSpan WindowSize { get; set; } = TimeSpan.FromMinutes(1);
    public int MaxIdentifiers { get; set; } = 100_000;
    public bool CountFailuresOnly { get; set; }
    public Func<HttpContext, bool> AppliesTo { get; set; } = static _ => true;
    public Func<HttpContext, string?> IdentifierSelector { get; set; } = static context =>
        IpNormalizer.Normalize(context.Connection.RemoteIpAddress?.ToString());
    public Func<HttpContext, int, bool> IsFailureResponse { get; set; } = static (_, statusCode) => statusCode >= 400;
}

public sealed class PubSoftRateLimitPolicy
{
    private readonly Func<HttpContext, bool> _appliesTo;
    private readonly Func<HttpContext, string?> _identifierSelector;
    private readonly Func<HttpContext, int, bool> _isFailureResponse;
    private readonly RateLimiter _limiter;

    public PubSoftRateLimitPolicy(PubSoftRateLimitPolicyOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Name);
        if (options.Name != options.Name.Trim())
            throw new ArgumentException("Policy names must have no surrounding whitespace.", nameof(options));

        Name = options.Name;
        CountFailuresOnly = options.CountFailuresOnly;
        _appliesTo = options.AppliesTo ?? throw new ArgumentException("A policy matcher is required.", nameof(options));
        _identifierSelector = options.IdentifierSelector ?? throw new ArgumentException("An identifier selector is required.", nameof(options));
        _isFailureResponse = options.IsFailureResponse ?? throw new ArgumentException("A failure response selector is required.", nameof(options));
        _limiter = new RateLimiter(new RateLimiterOptions
        {
            RequestsPerWindow = options.RequestsPerWindow,
            WindowSize = options.WindowSize,
            MaxIdentifiers = options.MaxIdentifiers
        });
    }

    public string Name { get; }
    public bool CountFailuresOnly { get; }
    internal bool Applies(HttpContext context) => _appliesTo(context);
    internal string? GetIdentifier(HttpContext context) => _identifierSelector(context);
    internal bool IsFailure(HttpContext context, int statusCode) => _isFailureResponse(context, statusCode);
    internal RateLimitResult Check(string? identifier, bool countRequest) => _limiter.CheckRateLimit(identifier, countRequest);
}

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
            context.Response.Headers["RateLimit-Remaining"] = remaining.ToString(CultureInfo.InvariantCulture);
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
        context.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
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
        => Math.Max(0, (int)Math.Ceiling((resetTime - DateTime.UtcNow).TotalSeconds));
}

public static class PubSoftRateLimitPolicyExtensions
{
    public static IServiceCollection AddPubSoftRateLimitPolicy(
        this IServiceCollection services,
        Action<PubSoftRateLimitPolicyOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var options = new PubSoftRateLimitPolicyOptions();
        configure(options);
        var policy = new PubSoftRateLimitPolicy(options);
        if (services
            .Where(descriptor => descriptor.ServiceType == typeof(PubSoftRateLimitPolicy))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<PubSoftRateLimitPolicy>()
            .Any(existing => string.Equals(existing.Name, policy.Name, StringComparison.Ordinal)))
            throw new ArgumentException($"A rate-limit policy named '{policy.Name}' is already registered.", nameof(configure));

        services.AddSingleton(policy);
        return services;
    }

    public static IApplicationBuilder UsePubSoftRateLimitPolicies(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<PubSoftRateLimitPolicyMiddleware>();
    }
}

/// <summary>
/// Customizes the 429 rejection body written by the rate-limiting middlewares.
/// Status code and Retry-After are already set by the middleware; the writer owns the body only.
/// Register once via <see cref="PubSoftRateLimitRejectionExtensions.AddPubSoftRateLimitRejection"/>;
/// default body is <c>{"error":"rate_limit_exceeded"}</c>.
/// </summary>
public sealed class PubSoftRateLimitRejection
{
    public PubSoftRateLimitRejection(Func<HttpContext, Task> bodyWriter)
    {
        BodyWriter = bodyWriter ?? throw new ArgumentNullException(nameof(bodyWriter));
    }

    /// <summary>Writes the rejection response body (status 429 + Retry-After already set).</summary>
    public Func<HttpContext, Task> BodyWriter { get; }
}

public static class PubSoftRateLimitRejectionExtensions
{
    /// <summary>
    /// Registers a host-specific 429 body writer — e.g. an API error envelope
    /// (<c>{"code":429,"message":"..."}</c>) matching the host's response conventions.
    /// </summary>
    public static IServiceCollection AddPubSoftRateLimitRejection(
        this IServiceCollection services,
        Func<HttpContext, Task> bodyWriter)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(new PubSoftRateLimitRejection(bodyWriter));
        return services;
    }
}
