using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PubSoft.Authorization;


public static class PermissionModuleDiscoveryExtensions
{
    public static IServiceCollection AddPermissionModuleDiscovery(
        this IServiceCollection services, Action<PermissionModuleDiscoveryOptions>? configure = null)
    {
        if (configure is not null)
            services.Configure(configure);
        services.AddSingleton<PermissionModuleDiscovery>();
        return services;
    }

    public static IServiceCollection AddPermissionModuleAutoRegistration(
        this IServiceCollection services)
    {
        services.AddHostedService<PermissionModuleAutoRegistrar>();
        return services;
    }
}
