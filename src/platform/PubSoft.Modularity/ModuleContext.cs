using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PubSoft.Modularity;

public class ModuleContext
{
    public required IServiceCollection Services { get; init; }
    public IServiceProvider? ServiceProvider { get; init; }
    public IConfiguration? Configuration { get; init; }
    public ILogger? Logger { get; init; }
    public CancellationToken ShutdownToken { get; init; }
}

public static class ModuleContextExtensions
{
    /// <summary>
    /// Resolves the host configuration — the explicit <see cref="ModuleContext.Configuration"/>,
    /// then <see cref="IConfiguration"/> from the service provider. Throws when neither is
    /// available rather than building a shadow provider from the service collection.
    /// </summary>
    public static IConfiguration GetConfiguration(this ModuleContext context)
        => context.Configuration
           ?? context.ServiceProvider?.GetService<IConfiguration>()
           ?? throw new InvalidOperationException(
               "No configuration on the module context — pass IConfiguration to AddPubSoftModules or set ModuleContext.Configuration.");

    /// <summary>Resolves <see cref="IHostEnvironment"/> from the service provider.</summary>
    public static IHostEnvironment GetEnv(this ModuleContext context)
        => context.ServiceProvider?.GetService<IHostEnvironment>()
           ?? throw new InvalidOperationException(
               "IHostEnvironment is not registered — module initialization requires a built service provider.");
}
