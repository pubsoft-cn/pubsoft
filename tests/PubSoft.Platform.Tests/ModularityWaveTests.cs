using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.AspNetCore;
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

    private sealed class AsyncModule : IPubSoftModule
    {
        public bool AsyncInitRan;
        public bool AsyncShutdownRan;
        public void ConfigureServices(ModuleContext ctx) { }
        public async ValueTask OnInitializationAsync(ModuleContext ctx)
        {
            await Task.Yield();
            AsyncInitRan = true;
        }
        public async ValueTask OnShutdownAsync(ModuleContext ctx)
        {
            await Task.Yield();
            AsyncShutdownRan = true;
        }
    }

    private sealed class AsyncThrowModule : IPubSoftModule
    {
        public void ConfigureServices(ModuleContext ctx) { }
        public async ValueTask OnInitializationAsync(ModuleContext ctx)
        {
            await Task.Yield();
            throw new InvalidOperationException("post-await failure");
        }
    }

    private sealed class ConfigProbeModule : IPubSoftModule
    {
        public IConfiguration? Seen;
        public void ConfigureServices(ModuleContext ctx) => Seen = ctx.GetConfiguration();
    }

    [Fact]
    public async Task AsyncHooksAwaitedByOrchestrator()
    {
        var m = new AsyncModule();
        var orch = new ModuleOrchestrator([ModuleDescriptor.FromType(typeof(AsyncModule))], _ => m);
        var ctx = Ctx();
        await orch.OnInitializationAllAsync(ctx);
        await orch.OnShutdownAllAsync(ctx);
        Assert.True(m.AsyncInitRan);
        Assert.True(m.AsyncShutdownRan);
    }

    [Fact]
    public async Task OptionalModulePostAwaitFailureDegradesInsteadOfThrowing()
    {
        var orch = new ModuleOrchestrator(
            [ModuleDescriptor.FromType(typeof(AsyncThrowModule), optional: true)],
            _ => new AsyncThrowModule());
        // async void would escape this catch — the async hook keeps degradation semantics
        await orch.OnInitializationAllAsync(Ctx());
        Assert.Single(orch.FailedModules);
    }

    [Fact]
    public void GetConfigurationResolvesExplicitThenProvider()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["k"] = "v" }).Build();
        var explicitCtx = new ModuleContext { Services = new ServiceCollection(), Configuration = config };
        Assert.Same(config, explicitCtx.GetConfiguration());

        var sp = new ServiceCollection().AddSingleton<IConfiguration>(config).BuildServiceProvider();
        var providerCtx = new ModuleContext { Services = new ServiceCollection(), ServiceProvider = sp };
        Assert.Same(config, providerCtx.GetConfiguration());

        Assert.Throws<InvalidOperationException>(() => Ctx().GetConfiguration());
    }

    [Fact]
    public async Task AddPubSoftModulesRunsConfigureThenInitWithGlue()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["k"] = "v" }).Build();
        var services = new ServiceCollection();
        services.AddPubSoftModules(config, m => m.Add<ConfigProbeModule>().Add<AsyncModule>());

        using var provider = services.BuildServiceProvider();
        var runtime = provider.GetRequiredService<PubSoftModuleRuntime>();
        runtime.ServiceProvider = provider;
        var orch = runtime.Orchestrator;
        var probe = Assert.IsType<ConfigProbeModule>(orch.GetModule(typeof(ConfigProbeModule)));
        Assert.Same(config, probe.Seen); // bound from the registration-time IConfiguration

        var ctx = new ModuleContext
        {
            Services = runtime.Services,
            ServiceProvider = provider
        };
        await orch.OnInitializationAllAsync(ctx);
        Assert.True(Assert.IsType<AsyncModule>(orch.GetModule(typeof(AsyncModule))).AsyncInitRan);
    }

    [Fact]
    public async Task WebGlueFillsAppBuilderAndEnvironment()
    {
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder();
        builder.Services.AddPubSoftModules(builder.Configuration, m => m.Add<WebProbeModule>());
        var app = builder.Build();
        await app.UsePubSoftModulesAsync();
        var probe = Assert.IsType<WebProbeModule>(
            app.Services.GetRequiredService<ModuleOrchestrator>().GetModule(typeof(WebProbeModule)));
        Assert.Same(app, probe.App);
        Assert.NotNull(probe.Env);
        await app.DisposeAsync();
    }

    private sealed class WebProbeModule : IPubSoftModule
    {
        public IApplicationBuilder? App;
        public Microsoft.Extensions.Hosting.IHostEnvironment? Env;
        public void ConfigureServices(ModuleContext ctx) { }
        public void OnInitialization(ModuleContext ctx)
        {
            App = ctx.GetAppBuilder();
            Env = ctx.GetEnv();
        }
    }
}
