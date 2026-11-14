using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PubSoft.Edge;


/// <summary>
/// DI extensions for the Edge gateway.
/// </summary>
public static class EdgeGatewayExtensions
{
    /// <summary>Registers an Edge handler (auto-added to the registry).</summary>
    public static IServiceCollection AddEdgeHandler<THandler>(this IServiceCollection services)
        where THandler : class, IEdgeHandler
    {
        services.AddTransient<THandler>();
        services.AddSingleton<IEdgeHandler>(sp => sp.GetRequiredService<THandler>());
        return services;
    }

    /// <summary>Registers the Edge gateway infrastructure.</summary>
    public static IServiceCollection AddEdgeGateway(this IServiceCollection services)
    {
        services.AddSingleton<EdgeHandlerRegistry>(sp =>
        {
            var registry = new EdgeHandlerRegistry();
            foreach (var handler in sp.GetServices<IEdgeHandler>())
                registry.Register(handler);
            return registry;
        });
        return services;
    }

    /// <summary>Registers the Edge gateway infrastructure with explicit options.</summary>
    public static IServiceCollection AddEdgeGateway(this IServiceCollection services, Action<EdgeGatewayOptions> configure)
    {
        services.Configure(configure);

        services.AddSingleton<EdgeHandlerRegistry>(sp =>
        {
            var registry = new EdgeHandlerRegistry();
            foreach (var handler in sp.GetServices<IEdgeHandler>())
                registry.Register(handler);
            return registry;
        });
        return services;
    }

    /// <summary>Binds gateway options from configuration.</summary>
    public static IServiceCollection AddEdgeGateway(this IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        services.Configure<EdgeGatewayOptions>(configuration.GetSection(EdgeGatewayOptions.SectionName));
        return AddEdgeGateway(services);
    }

    /// <summary>Installs the Edge gateway middleware.</summary>
    public static IApplicationBuilder UseEdgeGateway(this IApplicationBuilder app, string path)
    {
        return app.UseMiddleware<EdgeGatewayMiddleware>(path);
    }
}
