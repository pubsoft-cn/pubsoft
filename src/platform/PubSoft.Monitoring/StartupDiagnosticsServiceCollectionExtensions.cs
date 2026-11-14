using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PubSoft.Monitoring;


/// <summary>Startup diagnostics registration.</summary>
public static class StartupDiagnosticsServiceCollectionExtensions
{
    /// <summary>Registers the <see cref="StartupDiagnosticsCollector"/> and one provider instance.</summary>
    public static IServiceCollection AddPubSoftStartupDiagnostics<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
            this IServiceCollection services)
        where T : class, IStartupDiagnostics
    {
        services.TryAddSingleton<StartupDiagnosticsCollector>();
        services.AddSingleton<IStartupDiagnostics, T>();
        return services;
    }

    /// <summary>Registers the hosted service that logs every collected entry once at startup.</summary>
    public static IServiceCollection AddPubSoftStartupDiagnosticsHostedService(this IServiceCollection services)
    {
        services.TryAddSingleton<StartupDiagnosticsCollector>();
        services.AddHostedService<StartupDiagnosticsHostedService>();
        return services;
    }
}
