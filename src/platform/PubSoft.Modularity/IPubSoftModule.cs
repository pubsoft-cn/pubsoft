namespace PubSoft.Modularity;

public interface IPubSoftModule
{
    void ConfigureServices(ModuleContext context);
    void OnInitialization(ModuleContext context);
    void OnShutdown(ModuleContext context);
}
