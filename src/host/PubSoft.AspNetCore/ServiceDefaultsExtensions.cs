using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PubSoft.Monitoring;

namespace PubSoft.AspNetCore;

public static class ServiceDefaultsExtensions
{
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

    public static IApplicationBuilder UsePubSoftServiceDefaults(this IApplicationBuilder app)
    {
        app.UsePubSoftGatewayRateLimiting();
        app.UseExceptionHandler();
        return app;
    }

    public static WebApplication UsePubSoftServiceDefaults(this WebApplication app)
    {
        ((IApplicationBuilder)app).UsePubSoftServiceDefaults();
        app.MapPubSoftHealthEndpoint();
        return app;
    }
}
