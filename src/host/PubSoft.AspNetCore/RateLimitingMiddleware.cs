using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Security;
using System.Globalization;

namespace PubSoft.AspNetCore;

public sealed class PubSoftRateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RateLimiter _rateLimiter;

    public PubSoftRateLimitingMiddleware(RequestDelegate next, RateLimiter rateLimiter)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
    }

    public async Task InvokeAsync(HttpContext context)
    {
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
            await context.Response.WriteAsJsonAsync(
                new { error = "rate_limit_exceeded" },
                context.RequestAborted).ConfigureAwait(false);
            return;
        }

        await _next(context).ConfigureAwait(false);
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
    public Func<int, bool> IsFailureResponse { get; set; } = static statusCode => statusCode >= 400;
}

public sealed class PubSoftRateLimitPolicy
{
    private readonly Func<HttpContext, bool> _appliesTo;
    private readonly Func<HttpContext, string?> _identifierSelector;
    private readonly Func<int, bool> _isFailureResponse;
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
    internal bool IsFailure(int statusCode) => _isFailureResponse(statusCode);
    internal RateLimitResult Check(string? identifier, bool countRequest) => _limiter.CheckRateLimit(identifier, countRequest);
}

public sealed class PubSoftRateLimitPolicyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IReadOnlyList<PubSoftRateLimitPolicy> _policies;

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
            RecordFailures(failureCounters);
            throw;
        }

        RecordFailures(failureCounters, context.Response.StatusCode);
    }

    private static void RecordFailures(
        IEnumerable<(PubSoftRateLimitPolicy Policy, string? Identifier)> failureCounters,
        int? statusCode = null)
    {
        foreach (var (policy, identifier) in failureCounters)
        {
            if (statusCode is null || policy.IsFailure(statusCode.Value))
                _ = policy.Check(identifier, countRequest: true);
        }
    }

    private static async Task WriteRateLimitResponseAsync(HttpContext context, DateTime resetTime)
    {
        var retryAfter = Math.Max(1, GetSecondsUntilReset(resetTime));
        context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
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