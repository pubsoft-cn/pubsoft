using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace PubSoft.Caching.Redis;


/// <summary>Redis lock registration.</summary>
public static class RedisDistributedLockServiceCollectionExtensions
{
    /// <summary>
    /// Registers a Redis-backed <see cref="IDistributedLock"/> using the shared
    /// <see cref="IConnectionMultiplexer"/> supplied by the host.
    /// </summary>
    public static IServiceCollection AddPubSoftRedisDistributedLock(
        this IServiceCollection services,
        IConnectionMultiplexer connection,
        Action<RedisDistributedLockOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connection);

        var options = new RedisDistributedLockOptions();
        configure?.Invoke(options);
        services.Replace(ServiceDescriptor.Singleton<IDistributedLock>(new RedisDistributedLock(connection, options)));
        return services;
    }
}
