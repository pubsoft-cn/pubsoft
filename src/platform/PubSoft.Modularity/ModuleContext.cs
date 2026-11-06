using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PubSoft.Modularity;

/// <summary>Host surfaces handed to modules (services, config, logger, shutdown).</summary>
public class ModuleContext
{
    /// <summary>Registration-time service collection.</summary>
    public required IServiceCollection Services { get; init; }
    /// <summary>Built provider (set for init/shutdown phases).</summary>
    public IServiceProvider? ServiceProvider { get; init; }
    /// <summary>Host configuration.</summary>
    public IConfiguration? Configuration { get; init; }
    /// <summary>Host logger.</summary>
    public ILogger? Logger { get; init; }
    /// <summary>Fires on host shutdown.</summary>
    public CancellationToken ShutdownToken { get; init; }
}

/// <summary>ModuleContext resolution helpers.</summary>
public static class ModuleContextExtensions
{
    /// <summary>
    /// Resolves the host configuration -- the explicit <see cref="ModuleContext.Configuration"/>,
    /// then <see cref="IConfiguration"/> from the service provider. Throws when neither is
    /// available rather than building a shadow provider from the service collection.
    /// </summary>
    public static IConfiguration GetConfiguration(this ModuleContext context)
        => context.Configuration
           ?? context.ServiceProvider?.GetService<IConfiguration>()
           ?? throw new InvalidOperationException(
               "No configuration on the module context - pass IConfiguration to AddPubSoftModules or set ModuleContext.Configuration.");

    /// <summary>Resolves <see cref="IHostEnvironment"/> from the service provider.</summary>
    public static IHostEnvironment GetEnv(this ModuleContext context)
        => context.ServiceProvider?.GetService<IHostEnvironment>()
           ?? throw new InvalidOperationException(
               "IHostEnvironment is not registered - module initialization requires a built service provider.");
}
