namespace PubSoft.Modularity;

/// <summary>Module contract: service wiring + lifecycle hooks.</summary>
public interface IPubSoftModule
{
    /// <summary>Registers the module's services.</summary>
    void ConfigureServices(ModuleContext context);
    /// <summary>Post-build initialization hook.</summary>
    void OnInitialization(ModuleContext context) { }
    /// <summary>Shutdown hook.</summary>
    void OnShutdown(ModuleContext context) { }

    /// <summary>
    /// Async initialization hook -- the default bridges to <see cref="OnInitialization"/>.
    /// Prefer this over an <c>async void</c> sync override: exceptions after the first await
    /// escape the orchestrator's optional-module degradation filter and tear down the process.
    /// </summary>
    ValueTask OnInitializationAsync(ModuleContext context) { OnInitialization(context); return default; }

    /// <summary>Async shutdown hook -- the default bridges to <see cref="OnShutdown"/>.</summary>
    ValueTask OnShutdownAsync(ModuleContext context) { OnShutdown(context); return default; }
}
