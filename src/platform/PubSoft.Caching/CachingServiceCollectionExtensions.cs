using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PubSoft.Caching;

public static class CachingServiceCollectionExtensions
{
    public static IServiceCollection AddPubSoftMemoryCache(
        this IServiceCollection services,
        TimeSpan? cleanupInterval = null,
        int maxSize = 10000)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ICache>(_ => new MemoryCache(cleanupInterval, maxSize));
        return services;
    }

    /// <summary>
    /// Registers a single-process <see cref="IDistributedLock"/>. Development and single-instance
    /// deployments only — provides no cross-process mutual exclusion or fencing.
    /// </summary>
    public static IServiceCollection AddPubSoftInProcessDistributedLock(
        this IServiceCollection services,
        int maxEntries = 4096)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IDistributedLock>(_ => new InProcessDistributedLock(maxEntries: maxEntries));
        return services;
    }
}
