using Microsoft.Extensions.DependencyInjection;
using PubSoft.Modularity;

var calls = new List<string>();
var registry = new Dictionary<Type, IPubSoftModule>
{
    [typeof(ModuleA)] = new SmokeModule("a", calls),
    [typeof(ModuleB)] = new SmokeModule("b", calls),
    [typeof(ModuleC)] = new SmokeModule("c", calls),
};
var descriptors = new[]
{
    new ModuleDescriptor("base", typeof(ModuleA)),
    new ModuleDescriptor("mid", typeof(ModuleB), new[] { typeof(ModuleA) }),
    new ModuleDescriptor("top", typeof(ModuleC), new[] { typeof(ModuleB) }, Optional: true),
};
var orchestrator = new ModuleOrchestrator(descriptors, t => registry[t]);
var ctx = new ModuleContext { Services = new ServiceCollection() };

orchestrator.ConfigureServicesAll(ctx);
orchestrator.OnInitializationAll(ctx);
orchestrator.OnShutdownAll(ctx);

var expected = "a.cs,b.cs,c.cs,a.init,b.init,c.init,c.sd,b.sd,a.sd";
var actual = string.Join(',', calls);
if (actual != expected) throw new InvalidOperationException($"order mismatch: {actual}");

Console.WriteLine("PubSoft.Modularity consumer smoke passed.");

sealed class ModuleA : IPubSoftModule
{
    public void ConfigureServices(ModuleContext ctx) { }
    public void OnInitialization(ModuleContext ctx) { }
    public void OnShutdown(ModuleContext ctx) { }
}
sealed class ModuleB : IPubSoftModule
{
    public void ConfigureServices(ModuleContext ctx) { }
    public void OnInitialization(ModuleContext ctx) { }
    public void OnShutdown(ModuleContext ctx) { }
}
sealed class ModuleC : IPubSoftModule
{
    public void ConfigureServices(ModuleContext ctx) { }
    public void OnInitialization(ModuleContext ctx) { }
    public void OnShutdown(ModuleContext ctx) { }
}

sealed class SmokeModule : IPubSoftModule
{
    private readonly string _n;
    private readonly List<string> _calls;
    public SmokeModule(string n, List<string> calls) { _n = n; _calls = calls; }
    public void ConfigureServices(ModuleContext ctx) => _calls.Add(_n + ".cs");
    public void OnInitialization(ModuleContext ctx) => _calls.Add(_n + ".init");
    public void OnShutdown(ModuleContext ctx) => _calls.Add(_n + ".sd");
}
