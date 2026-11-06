using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PubSoft.Authorization;


/// <summary>Hosted service that scans and registers permission modules at startup.</summary>
public sealed class PermissionModuleAutoRegistrar(
    PermissionModuleDiscovery discovery,
    IEnumerable<IPermissionModuleRegistrationSink> sinks,
    ILogger<PermissionModuleAutoRegistrar> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var sink = sinks.FirstOrDefault();
        if (sink is null)
        {
            logger.LogDebug("permission module auto-registration skipped - no sink registered");
            return;
        }
        try
        {
            var modules = discovery.Scan();
            await sink.RegisterAsync(modules, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("permission module auto-registration completed: {Count} modules", modules.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "permission module auto-registration failed - startup continues");
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
