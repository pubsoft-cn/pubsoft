using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace PubSoft.Scheduling;

public static class SchedulerServiceCollectionExtensions
{
    public static IServiceCollection AddPubSoftScheduler(
        this IServiceCollection services,
        Action<SchedulerOptions>? configure = null)
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

    public static IServiceCollection AddExecutor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TExecutor>(this IServiceCollection services)
        where TExecutor : class, IJobExecutor
    {
        services.AddSingleton<IJobExecutor, TExecutor>();
        return services;
    }

    public static IServiceCollection AddResultProcessor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProcessor>(this IServiceCollection services)
        where TProcessor : class, IResultProcessor
    {
        services.AddSingleton<IResultProcessor, TProcessor>();
        return services;
    }
}