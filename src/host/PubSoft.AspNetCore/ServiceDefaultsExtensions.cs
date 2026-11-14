using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Monitoring;

namespace PubSoft.AspNetCore;

/// <summary>Opinionated host defaults (headers, limits, tenant context).</summary>
public static class ServiceDefaultsExtensions
{
    /// <summary>Registers the default service set.</summary>
    public static IServiceCollection AddPubSoftServiceDefaults(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddMemoryCache();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddProblemDetails();
        services.AddPubSoftHealthChecks();
        services.AddPubSoftGatewayRateLimiting(configuration);
        return services;
    }

    /// <summary>Adds the default middleware pipeline.</summary>
    public static IApplicationBuilder UsePubSoftServiceDefaults(this IApplicationBuilder app)
    {
        app.UsePubSoftGatewayRateLimiting();
        app.UseExceptionHandler();
        return app;
    }

    /// <summary>Adds the default middleware pipeline.</summary>
    public static WebApplication UsePubSoftServiceDefaults(this WebApplication app)
    {
        ((IApplicationBuilder)app).UsePubSoftServiceDefaults();
        app.MapPubSoftHealthEndpoint();
        return app;
    }
}
