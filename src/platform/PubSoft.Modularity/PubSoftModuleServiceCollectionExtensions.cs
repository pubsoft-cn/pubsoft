using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PubSoft.Modularity;


/// <summary>Module registration.</summary>
public static class PubSoftModuleServiceCollectionExtensions
{
    /// <summary>Registers modules and runs their <c>ConfigureServices</c> in dependency order.</summary>
    public static IServiceCollection AddPubSoftModules(
        this IServiceCollection services,
        Action<PubSoftModuleBuilder> configure)
        => services.AddPubSoftModules(configuration: null, configure);

    /// <summary>
    /// Registers modules, binds <see cref="ModuleContext.Configuration"/>, and runs their
    /// <c>ConfigureServices</c> in dependency order. Pair with <c>UsePubSoftModulesAsync</c>
    /// (web) or <see cref="StartPubSoftModulesAsync"/> (generic host) for the runtime phases.
    /// </summary>
    public static IServiceCollection AddPubSoftModules(
        this IServiceCollection services,
        IConfiguration? configuration,
        Action<PubSoftModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new PubSoftModuleBuilder();
        configure(builder);
        var runtime = new PubSoftModuleRuntime(
            new ModuleOrchestrator(builder.Descriptors, builder.Create), services);
        builder.Runtime = runtime;
        services.TryAddSingleton(runtime);
        services.TryAddSingleton(runtime.Orchestrator);
        runtime.Orchestrator.ConfigureServicesAll(new ModuleContext
        {
            Services = services,
            Configuration = configuration
        });
        return services;
    }

    /// <summary>
    /// Runs <c>OnInitialization</c> for all modules on a non-web <see cref="IHost"/> and hooks
    /// reverse-order <c>OnShutdown</c> to <see cref="IHostApplicationLifetime.ApplicationStopping"/>.
    /// </summary>
    public static async Task<IHost> StartPubSoftModulesAsync(this IHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var runtime = host.Services.GetRequiredService<PubSoftModuleRuntime>();
        runtime.ServiceProvider = host.Services;
        var lifetime = host.Services.GetService<IHostApplicationLifetime>();
        var context = new ModuleContext
        {
            Services = runtime.Services,
            ServiceProvider = host.Services,
            Configuration = host.Services.GetService<IConfiguration>(),
            Logger = host.Services.GetService<ILoggerFactory>()?.CreateLogger("PubSoft.Modularity"),
            ShutdownToken = lifetime?.ApplicationStopping ?? default
        };
        await runtime.Orchestrator.OnInitializationAllAsync(context).ConfigureAwait(false);
        lifetime?.ApplicationStopping.Register(
            () => runtime.Orchestrator.OnShutdownAllAsync(context).AsTask().GetAwaiter().GetResult()); // pubsoft:exempt PUBSOFT017 "intentional sync bridge - callback is sync by contract"
        return host;
    }
}
