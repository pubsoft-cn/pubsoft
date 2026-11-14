using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Security;

namespace PubSoft.AspNetCore;


/// <summary>Registration for the fixed-window rate limiter.</summary>
public static class PubSoftRateLimitingExtensions
{
    /// <summary>Registers the rate-limiting middleware with options.</summary>
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

    /// <summary>Adds the rate-limiting middleware.</summary>
    public static IApplicationBuilder UsePubSoftRateLimiting(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<PubSoftRateLimitingMiddleware>();
    }
}
