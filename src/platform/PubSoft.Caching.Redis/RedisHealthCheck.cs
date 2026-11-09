using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace PubSoft.Caching.Redis;

/// <summary>PINGs the shared Redis multiplexer and reports latency - the Redis counterpart of <c>EfCoreHealthCheck</c>.</summary>
public sealed class RedisHealthCheck(IConnectionMultiplexer multiplexer) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var db = multiplexer.GetDatabase();
        var latency = await db.PingAsync().ConfigureAwait(false);
        return HealthCheckResult.Healthy($"Redis OK ({latency.TotalMilliseconds:F1} ms)");
    }
}

/// <summary>Redis health-check registration.</summary>
public static class RedisHealthCheckExtensions
{
    /// <summary>Registers the Redis PING health check against the registered <see cref="IConnectionMultiplexer"/>.</summary>
    public static IHealthChecksBuilder AddPubSoftRedisCheck(this IHealthChecksBuilder builder, string name = "redis")
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.AddCheck<RedisHealthCheck>(name);
    }
}
