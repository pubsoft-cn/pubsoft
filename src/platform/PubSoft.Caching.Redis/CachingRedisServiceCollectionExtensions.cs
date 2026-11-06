using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Caching;
using StackExchange.Redis;

namespace PubSoft.Caching.Redis;

/// <summary>Redis cache registration.</summary>
public static class CachingRedisServiceCollectionExtensions
{
    /// <summary>Registers the Redis <see cref="ICache"/>.</summary>
    public static IServiceCollection AddPubSoftRedisCache(
        this IServiceCollection services,
        IConnectionMultiplexer connection,
        Action<RedisCacheOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connection);

        var options = new RedisCacheOptions();
        configure?.Invoke(options);
        services.Replace(ServiceDescriptor.Singleton<ICache>(new RedisCache(connection, options)));
        return services;
    }
}