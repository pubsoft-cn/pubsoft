using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace PubSoft.Security.Redis;

/// <summary>DI registration for the Redis token denylist.</summary>
public static class RedisTokenDenylistServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="RedisTokenDenylist"/> as the shared <see cref="ITokenDenylist"/>
    /// over an existing connection. The denylist is the revocation source of truth - all
    /// validator instances must share the same Redis, database, and prefix.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="connection">Shared connection multiplexer.</param>
    /// <param name="configure">Optional bounded options.</param>
    public static IServiceCollection AddPubSoftRedisTokenDenylist(this IServiceCollection services, IConnectionMultiplexer connection, Action<RedisTokenDenylistOptions>? configure = null)
    {
        var options = new RedisTokenDenylistOptions();
        configure?.Invoke(options);
        options.Validate();
        ArgumentNullException.ThrowIfNull(connection);
        services.TryAddSingleton<ITokenDenylist>(_ => new RedisTokenDenylist(connection, options));
        return services;
    }
}
