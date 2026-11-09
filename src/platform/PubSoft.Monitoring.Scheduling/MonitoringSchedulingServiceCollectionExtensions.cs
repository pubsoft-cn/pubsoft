using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Scheduling;

namespace PubSoft.Monitoring.Scheduling;

/// <summary>Registration for the scheduling-alert integration.</summary>
public static class MonitoringSchedulingServiceCollectionExtensions
{
    /// <summary>Registers <see cref="AlertRuleProcessor"/> with validated options.</summary>
    public static IServiceCollection AddPubSoftMonitoringScheduling(
        this IServiceCollection services,
        Action<AlertProcessorOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddOptions<AlertProcessorOptions>()
            .Configure(options => configure?.Invoke(options))
            .Validate(options => options.IsValid(), "Monitoring alert processor options are invalid.")
            .ValidateOnStart();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IResultProcessor, AlertRuleProcessor>());
        return services;
    }
}