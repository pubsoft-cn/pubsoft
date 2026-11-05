namespace PubSoft.Modularity;

public interface IPubSoftModule
{
    void ConfigureServices(ModuleContext context);
    void OnInitialization(ModuleContext context) { }
    void OnShutdown(ModuleContext context) { }

    /// <summary>
    /// Async initialization hook — the default bridges to <see cref="OnInitialization"/>.
    /// Prefer this over an <c>async void</c> sync override: exceptions after the first await
    /// escape the orchestrator's optional-module degradation filter and tear down the process.
    /// </summary>
    ValueTask OnInitializationAsync(ModuleContext context) { OnInitialization(context); return default; }

    /// <summary>Async shutdown hook — the default bridges to <see cref="OnShutdown"/>.</summary>
    ValueTask OnShutdownAsync(ModuleContext context) { OnShutdown(context); return default; }
}
