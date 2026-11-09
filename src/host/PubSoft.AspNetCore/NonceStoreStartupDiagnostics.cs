using Microsoft.Extensions.DependencyInjection;
using PubSoft.Monitoring;
using PubSoft.Security;

namespace PubSoft.AspNetCore;

/// <summary>Reports when HMAC replay protection runs on the process-local nonce store.</summary>
public sealed class NonceStoreStartupDiagnostics : IStartupDiagnostics
{
    private readonly IServiceProvider _services;

    /// <summary>Creates the provider.</summary>
    public NonceStoreStartupDiagnostics(IServiceProvider services) => _services = services;

    /// <inheritdoc />
    public IEnumerable<StartupDiagnosticsEntry> Collect()
    {
        var store = _services.GetService<INonceStore>();
        if (store is null || store is MemoryNonceStore)
            yield return new StartupDiagnosticsEntry("hmac-nonce",
                StartupDiagnosticsStatus.Warning,
                "INonceStore is process-local MemoryNonceStore - replay protection is per-instance; register a shared store (e.g. Redis) when running multiple replicas");
    }
}
