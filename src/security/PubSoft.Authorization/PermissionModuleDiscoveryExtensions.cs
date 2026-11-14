using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PubSoft.Authorization;


/// <summary>Permission discovery registration.</summary>
public static class PermissionModuleDiscoveryExtensions
{
    /// <summary>Registers the module scanner.</summary>
    public static IServiceCollection AddPermissionModuleDiscovery(
        this IServiceCollection services, Action<PermissionModuleDiscoveryOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);
        services.AddSingleton<PermissionModuleDiscovery>();
        return services;
    }

    /// <summary>Registers the hosted auto-registration service.</summary>
    public static IServiceCollection AddPermissionModuleAutoRegistration(
        this IServiceCollection services)
    {
        services.AddHostedService<PermissionModuleAutoRegistrar>();
        return services;
    }
}
