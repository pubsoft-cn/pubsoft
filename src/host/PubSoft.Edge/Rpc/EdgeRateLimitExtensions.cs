using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PubSoft.Edge;


/// <summary>
/// Edge rate-limit DI extensions.
/// </summary>
public static class EdgeRateLimitExtensions
{
    /// <summary>Registers Edge rate limiting (options from configuration).</summary>
    public static IServiceCollection AddEdgeRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<EdgeRateLimitOptions>(configuration.GetSection(EdgeRateLimitOptions.SectionName));
        return services;
    }

    /// <summary>Registers Edge rate limiting (explicit configuration).</summary>
    public static IServiceCollection AddEdgeRateLimiting(
        this IServiceCollection services,
        Action<EdgeRateLimitOptions> configure)
    {
        services.Configure(configure);
        return services;
    }

    /// <summary>
    /// Installs the rate-limit middleware -- path must match UseEdgeGateway.
    /// <code>
    /// app.UseEdgeRateLimiting("/rpc");
    /// app.UseEdgeGateway("/rpc");
    /// </code>
    /// </summary>
    public static IApplicationBuilder UseEdgeRateLimiting(this IApplicationBuilder app, string path)
    {
        return app.UseMiddleware<EdgeRateLimitMiddleware>(path);
    }
}
