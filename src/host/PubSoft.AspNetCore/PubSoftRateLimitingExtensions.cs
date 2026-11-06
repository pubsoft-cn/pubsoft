using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PubSoft.Security;

namespace PubSoft.AspNetCore;


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
