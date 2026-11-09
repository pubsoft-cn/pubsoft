using Microsoft.Extensions.DependencyInjection;
using PubSoft.Monitoring;
using StackExchange.Redis;

namespace PubSoft.Caching.Redis;

/// <summary>Reports when no shared Redis connection is registered -- cache/lock paths diverge across replicas.</summary>
public sealed class RedisConnectionStartupDiagnostics : IStartupDiagnostics
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the provider.</summary>
    public RedisConnectionStartupDiagnostics(IServiceProvider services) => _services = services;

    /// <inheritdoc />
    public IEnumerable<StartupDiagnosticsEntry> Collect()
    {
        if (_services.GetService<IConnectionMultiplexer>() is null)
            yield return new StartupDiagnosticsEntry("redis-connection",
                StartupDiagnosticsStatus.Warning,
                "no IConnectionMultiplexer registered - cache, nonce and lock paths fall back to process-local state and diverge across replicas");
    }
}
