using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PubSoft.Caching;

namespace PubSoft.Scheduling;


/// <summary>Fenced executor and fenced worker registration.</summary>
public static class FencedWorkerServiceCollectionExtensions
{
    /// <summary>Registers the lock+run primitive usable inside scheduled jobs or ad-hoc call sites.</summary>
    public static IServiceCollection AddPubSoftFencedExecutor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(sp => new FencedExecutor(
            sp.GetRequiredService<IDistributedLock>(),
            sp.GetService<TimeProvider>()));
        services.TryAddSingleton<IFencedExecutor>(sp => sp.GetRequiredService<FencedExecutor>());
        return services;
    }

    /// <summary>
    /// Registers a periodic worker that runs <paramref name="work"/> under a distributed lock each
    /// interval. Requires an <see cref="IDistributedLock"/> registration (in-process for single
    /// hosts, Redis for multi-instance). The fencing token arrives on <see cref="FencedWorkContext"/>.
    /// </summary>
    public static IServiceCollection AddPubSoftFencedWorker(
        this IServiceCollection services,
        string name,
        Func<FencedWorkContext, CancellationToken, Task> work,
        Action<FencedWorkerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(work);

        var options = new FencedWorkerOptions();
        configure?.Invoke(options);
        options.Validate(name);
        services.AddPubSoftFencedExecutor();
        // factory registration: each named worker is a distinct hosted service
        services.AddSingleton<IHostedService>(sp =>
            new FencedWorkerHostedService(name, options, work,
                sp.GetRequiredService<IFencedExecutor>(),
                sp.GetService<ILogger<FencedWorkerHostedService>>()));
        return services;
    }
}
