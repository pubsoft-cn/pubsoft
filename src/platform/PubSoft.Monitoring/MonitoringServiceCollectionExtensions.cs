using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PubSoft.Monitoring;

public static class MonitoringServiceCollectionExtensions
{
    public static IServiceCollection AddPubSoftMonitoring(
        this IServiceCollection services,
        Action<MonitoringOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<MonitoringOptions>()
            .Configure(options => configure?.Invoke(options))
            .Validate(options => options.IsValid(out _), "Monitoring probe options are invalid.")
            .ValidateOnStart();
        services.AddHttpClient(MonitoringOptions.HttpClientName);
        services.TryAddSingleton<IProbeHealthCheck, HttpProbeHealthCheck>();
        return services;
    }

    public static IServiceCollection AddPubSoftMonitoringMetrics(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<MonitoringMetrics>();
        return services;
    }

    public static IServiceCollection AddPubSoftHealthChecks(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<HealthCheckAggregator>(provider =>
            new HealthCheckAggregator(provider.GetServices<IAppHealthCheck>()));
        return services;
    }

    public static IServiceCollection AddPubSoftCachedHealthChecks(
        this IServiceCollection services,
        TimeSpan? healthyCacheDuration = null,
        TimeSpan? unhealthyCacheDuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<CachedHealthCheckAggregator>(provider =>
            new CachedHealthCheckAggregator(
                provider.GetServices<IAppHealthCheck>(),
                healthyCacheDuration,
                unhealthyCacheDuration,
                provider.GetService<TimeProvider>()));
        return services;
    }

    public static IServiceCollection AddPubSoftParallelHealthChecks(
        this IServiceCollection services,
        TimeSpan? timeout = null,
        int maxConcurrency = 8)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ParallelHealthCheckAggregator>(provider =>
            new ParallelHealthCheckAggregator(
                provider.GetServices<IAppHealthCheck>(),
                timeout,
                maxConcurrency));
        return services;
    }

    public static IServiceCollection AddPubSoftHealthCheck<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TCheck>(this IServiceCollection services)
        where TCheck : class, IAppHealthCheck
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IAppHealthCheck, TCheck>();
        return services;
    }
}