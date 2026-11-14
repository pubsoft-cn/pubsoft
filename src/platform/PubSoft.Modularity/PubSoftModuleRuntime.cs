using Microsoft.Extensions.DependencyInjection;

namespace PubSoft.Modularity;


/// <summary>Shared state between <c>AddPubSoftModules</c> (registration) and the init glue (runtime).</summary>
public sealed class PubSoftModuleRuntime
{
    internal PubSoftModuleRuntime(ModuleOrchestrator orchestrator, IServiceCollection services)
    {
        Orchestrator = orchestrator;
        Services = services;
    }

    /// <summary>The running orchestrator.</summary>
    public ModuleOrchestrator Orchestrator { get; }

    /// <summary>The registration-time service collection, reused on init/shutdown contexts.</summary>
    public IServiceCollection Services { get; }

    /// <summary>Built provider -- set by the initialization glue before lifecycle hooks run.</summary>
    public IServiceProvider? ServiceProvider { get; set; }
}
