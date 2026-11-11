using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace PubSoft.Security.Redis;

/// <summary>DI registration for the Redis refresh-token family store.</summary>
public static class RedisRefreshTokenFamilyStoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="RedisRefreshTokenFamilyStore"/> as the shared
    /// <see cref="IRefreshTokenFamilyStore"/> over an existing connection. Use for multi-instance
    /// deployments; the consume script is atomic so reuse detection holds across instances.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="connection">Shared connection multiplexer.</param>
    /// <param name="configure">Optional bounded options.</param>
    public static IServiceCollection AddPubSoftRedisRefreshTokenFamilyStore(this IServiceCollection services, IConnectionMultiplexer connection, Action<RedisRefreshTokenFamilyStoreOptions>? configure = null)
    {
        var options = new RedisRefreshTokenFamilyStoreOptions();
        configure?.Invoke(options);
        options.Validate();
        ArgumentNullException.ThrowIfNull(connection);
        services.TryAddSingleton<IRefreshTokenFamilyStore>(_ => new RedisRefreshTokenFamilyStore(connection, options));
        return services;
    }
}
