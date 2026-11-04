using Microsoft.Extensions.DependencyInjection;
using PubSoft.Modularity;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class ModularityWaveTests
{
    private sealed class ProbeModule(string tag) : IPubSoftModule
    {
        public List<string> Calls { get; } = [];
        public void ConfigureServices(ModuleContext ctx) => Calls.Add($"configure:{tag}");
        public void OnInitialization(ModuleContext ctx) => Calls.Add($"init:{tag}");
        public void OnShutdown(ModuleContext ctx) => Calls.Add($"shutdown:{tag}");
    }

    private static ModuleContext Ctx() => new() { Services = new ServiceCollection() };

    [Fact]
    public void ModuleDescriptorFromType()
    {
        var d = ModuleDescriptor.FromType(typeof(ProbeModule));
        Assert.Equal("ProbeModule", d.Name);
        Assert.Equal(typeof(ProbeModule), d.ModuleType);
    }

    [Fact]
    public void OrchestratorRejectsDuplicateNames()
    {
        var d = ModuleDescriptor.FromType(typeof(ProbeModule));
        Assert.Throws<ArgumentException>(() =>
            new ModuleOrchestrator([d, d], t => new ProbeModule("x")));
    }

    [Fact]
    public void LifecycleHooksInvokeInOrder()
    {
        var m = new ProbeModule("m1");
        var orch = new ModuleOrchestrator([ModuleDescriptor.FromType(typeof(ProbeModule))], _ => m);
        var ctx = Ctx();
        orch.ConfigureServicesAll(ctx);
        orch.OnInitializationAll(ctx);
        orch.OnShutdownAll(ctx);
        Assert.Equal(["configure:m1", "init:m1", "shutdown:m1"], m.Calls);
        Assert.Equal(1, orch.ModuleCount);
        Assert.Empty(orch.FailedModules);
    }

    [Fact]
    public void ShutdownOrderReversesTopological()
    {
        var a = ModuleDescriptor.FromType(typeof(ModA));
        var b = ModuleDescriptor.FromType(typeof(ModB));
        var orch = new ModuleOrchestrator([a, b], t => new ProbeModule("x"),
            topologicalOrder: [typeof(ModA), typeof(ModB)]);
        Assert.Equal([typeof(ModA), typeof(ModB)], orch.TopologicalOrder);
        Assert.Equal([typeof(ModB), typeof(ModA)], orch.ShutdownOrder);
    }

    [Fact]
    public void GetModuleByType()
    {
        var m = new ProbeModule("x");
        var orch = new ModuleOrchestrator([ModuleDescriptor.FromType(typeof(ProbeModule))], _ => m);
        orch.ConfigureServicesAll(Ctx());
        Assert.Same(m, orch.GetModule<ProbeModule>());
    }

    private sealed class ModA : IPubSoftModule { public void ConfigureServices(ModuleContext ctx) { } }
    private sealed class ModB : IPubSoftModule { public void ConfigureServices(ModuleContext ctx) { } }
}
