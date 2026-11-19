using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace PubSoft.Caching.Redis;


/// <summary>
/// Configuration-driven <see cref="ICache"/> registration. Hosts call one method and select the
/// provider through configuration instead of branching per environment.
/// </summary>
/// <remarks>
/// Expected keys under the <c>Caching</c> section:
/// <list type="bullet">
/// <item><c>Provider</c> -- <c>memory</c> or <c>redis</c> (required; missing or unknown fails closed).</item>
/// <item><c>Memory:MaxSize</c>, <c>Memory:CleanupIntervalSeconds</c> -- in-memory cache bounds.</item>
/// <item><c>Redis:ConnectionString</c> -- required when Provider=redis; creates a DI-owned multiplexer.</item>
/// <item><c>Redis:Database</c>, <c>Redis:KeyPrefix</c>, <c>Redis:MaxValueBytes</c> -- <see cref="RedisCacheOptions"/> values.</item>
/// </list>
/// Bound manually (no reflection binder) so registration stays AOT-safe. In the redis lane the
/// multiplexer is registered through a factory: connecting is deferred to first resolution
/// instead of blocking the composition root, and the container owns disposal. To keep a
/// host-owned multiplexer, register <c>IConnectionMultiplexer</c> first - this path then reuses
/// it instead of connecting again.
/// </remarks>
public static class CacheConfigurationServiceCollectionExtensions
{
    /// <summary>Registers the configured <see cref="ICache"/> provider.</summary>
    /// <exception cref="InvalidOperationException">
    /// <c>Caching:Provider</c> missing/unknown, or <c>Caching:Redis:ConnectionString</c> missing
    /// when the redis provider is selected -- an absent provider must never silently fall back to
    /// in-process caching in a host that expects a shared store.
    /// </exception>
    public static IServiceCollection AddPubSoftCache(
        this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var provider = configuration["Caching:Provider"];
        if (string.IsNullOrWhiteSpace(provider))
            throw new InvalidOperationException(
                "Caching:Provider is required when calling AddPubSoftCache; expected 'memory' or 'redis'.");

        if (provider.Equals("memory", StringComparison.OrdinalIgnoreCase))
        {
            var maxSize = ReadInt(configuration, "Caching:Memory:MaxSize", 10_000);
            var cleanupSeconds = ReadInt(configuration, "Caching:Memory:CleanupIntervalSeconds", 300);
            return services.AddPubSoftMemoryCache(TimeSpan.FromSeconds(cleanupSeconds), maxSize);
        }

        if (provider.Equals("redis", StringComparison.OrdinalIgnoreCase))
        {
            var connectionString = configuration["Caching:Redis:ConnectionString"];
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "Caching:Redis:ConnectionString is required when Caching:Provider is 'redis'; " +
                    "register an IConnectionMultiplexer and call AddPubSoftRedisCache for advanced setups.");

            var options = new RedisCacheOptions
            {
                Database = ReadInt(configuration, "Caching:Redis:Database", -1),
                KeyPrefix = configuration["Caching:Redis:KeyPrefix"] ?? string.Empty,
                MaxValueBytes = ReadInt(configuration, "Caching:Redis:MaxValueBytes", 4_194_304)
            };
            services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connectionString));
            services.Replace(ServiceDescriptor.Singleton<ICache>(
                sp => new RedisCache(sp.GetRequiredService<IConnectionMultiplexer>(), options)));
            services.AddSingleton<PubSoft.Monitoring.IStartupDiagnostics, RedisConnectionStartupDiagnostics>();
            return services;
        }

        throw new InvalidOperationException(
            $"Unknown Caching:Provider '{provider}'; expected 'memory' or 'redis'.");
    }

    private static int ReadInt(IConfiguration configuration, string key, int fallback)
    {
        var raw = configuration[key];
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;
        if (!int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new InvalidOperationException($"Configuration key '{key}' must be an integer; got '{raw}'.");
        return value;
    }
}
