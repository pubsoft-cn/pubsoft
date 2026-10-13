using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Security;
using StackExchange.Redis;

namespace PubSoft.Security.Redis;

public static class RedisNonceStoreServiceCollectionExtensions
{
    public static IServiceCollection AddPubSoftRedisNonceStore(
        this IServiceCollection services,
        IConnectionMultiplexer connection,
        Action<RedisNonceStoreOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connection);

        var options = new RedisNonceStoreOptions();
        configure?.Invoke(options);
        var validatedOptions = options.CloneAndValidate();
        services.Replace(ServiceDescriptor.Singleton<INonceStore>(
            new RedisNonceStore(connection, validatedOptions)));
        return services;
    }
}