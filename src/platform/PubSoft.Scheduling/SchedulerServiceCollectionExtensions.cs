using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace PubSoft.Scheduling;

/// <summary>Scheduler registration.</summary>
public static class SchedulerServiceCollectionExtensions
{
    /// <summary>Registers the scheduler with in-process state store.</summary>
    public static IServiceCollection AddPubSoftScheduler(this IServiceCollection services)
        => services.AddPubSoftScheduler(configure: null);

    /// <summary>Registers the scheduler with configured options.</summary>
    public static IServiceCollection AddPubSoftScheduler(
        this IServiceCollection services,
        Action<SchedulerOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<SchedulerOptions>()
            .Configure(options => configure?.Invoke(options))
            .Validate(options => options.IsValid(out _), "Scheduler options are invalid.")
            .ValidateOnStart();
        services.TryAddSingleton<IExecutorRegistry, ExecutorRegistry>();
        services.TryAddSingleton<IResultProcessorRegistry, ResultProcessorRegistry>();
        services.TryAddSingleton<IStateStore, MemoryStateStore>();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<SchedulerEngine>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, SchedulerHostedService>());
        return services;
    }

    /// <summary>
    /// Registers the scheduler bound to a configuration section (expected children:
    /// <c>Jobs</c>, <c>LoopInterval</c>, <c>ErrorDelay</c>, <c>MaxConcurrentJobs</c>).
    /// Bound without the reflection-based binder so executor/result-processor JSON
    /// payloads and bitmask schedules round-trip correctly under AOT.
    /// </summary>
    public static IServiceCollection AddPubSoftScheduler(
        this IServiceCollection services,
        IConfiguration section)
    {
        ArgumentNullException.ThrowIfNull(section);
        return services.AddPubSoftScheduler(o =>
        {
            var bound = SchedulerConfigurationBinding.Bind(section);
            o.Jobs = bound.Jobs;
            o.LoopInterval = bound.LoopInterval;
            o.ErrorDelay = bound.ErrorDelay;
            o.MaxConcurrentJobs = bound.MaxConcurrentJobs;
        });
    }

    /// <summary>Registers a job executor type.</summary>
    public static IServiceCollection AddExecutor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TExecutor>(this IServiceCollection services)
        where TExecutor : class, IJobExecutor
    {
        services.AddSingleton<IJobExecutor, TExecutor>();
        return services;
    }

    /// <summary>Registers a result-processor type.</summary>
    public static IServiceCollection AddResultProcessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>(this IServiceCollection services)
        where TProcessor : class, IResultProcessor
    {
        services.AddSingleton<IResultProcessor, TProcessor>();
        return services;
    }
}