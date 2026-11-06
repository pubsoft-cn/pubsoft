using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PubSoft.Modularity;

/// <summary>Collects module descriptors for <c>AddPubSoftModules</c>.</summary>
public sealed class PubSoftModuleBuilder
{
    private readonly List<ModuleDescriptor> _descriptors = [];
    private readonly Dictionary<Type, Func<IServiceProvider?, IPubSoftModule>> _factories = new();

    /// <inheritdoc cref="Add{T}(bool)"/>
    public PubSoftModuleBuilder Add<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : IPubSoftModule
        => Add<T>(optional: false);

    /// <summary>
    /// Adds a module by type. During <c>ConfigureServices</c> the module is activated without a
    /// container (parameterless ctor); later phases activate it with <see cref="ActivatorUtilities"/>
    /// so constructor dependencies resolve. Types needing non-DI activation should use the
    /// factory overload.
    /// </summary>
    public PubSoftModuleBuilder Add<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        bool optional)
        where T : IPubSoftModule
        => Add(ModuleDescriptor.FromType(typeof(T), optional: optional),
            provider => (IPubSoftModule)(provider is { } sp
                ? ActivatorUtilities.CreateInstance(sp, typeof(T))
                : Activator.CreateInstance(typeof(T))!));

    /// <summary>Adds a module with an explicit factory — the AOT-explicit path.</summary>
    public PubSoftModuleBuilder Add(ModuleDescriptor descriptor, Func<IServiceProvider?, IPubSoftModule> factory)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(factory);
        _descriptors.Add(descriptor);
        _factories[descriptor.ModuleType] = factory;
        return this;
    }

    internal ModuleDescriptor[] Descriptors => _descriptors.ToArray();

    internal IPubSoftModule Create(Type moduleType)
        => _factories.TryGetValue(moduleType, out var factory)
            ? factory(Runtime?.ServiceProvider)
            : throw new InvalidOperationException($"No factory registered for module '{moduleType.Name}'.");

    internal PubSoftModuleRuntime? Runtime { get; set; }
}

/// <summary>Shared state between <c>AddPubSoftModules</c> (registration) and the init glue (runtime).</summary>
public sealed class PubSoftModuleRuntime
{
    internal PubSoftModuleRuntime(ModuleOrchestrator orchestrator, IServiceCollection services)
    {
        Orchestrator = orchestrator;
        Services = services;
    }

    public ModuleOrchestrator Orchestrator { get; }

    /// <summary>The registration-time service collection, reused on init/shutdown contexts.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Built provider — set by the initialization glue before lifecycle hooks run.</summary>
    public IServiceProvider? ServiceProvider { get; set; }
}

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
            () => runtime.Orchestrator.OnShutdownAllAsync(context).AsTask().GetAwaiter().GetResult()); // pubsoft:exempt PUBSOFT017 "intentional sync bridge — callback is sync by contract"
        return host;
    }
}
