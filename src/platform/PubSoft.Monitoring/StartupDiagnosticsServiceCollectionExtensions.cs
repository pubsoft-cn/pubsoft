using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PubSoft.Monitoring;


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
}
