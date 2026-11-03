using Microsoft.Extensions.DependencyInjection;
using PubSoft.Modularity;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class ModuleOrchestratorTests
{
    [Fact]
    public void TopologicalOrder_RespectsDependencies()
    {
        var o = Build(new[]
        {
            Desc<A>(), Desc<B>(typeof(A)), Desc<C>(typeof(B)),
        });

        Assert.Equal(new[] { typeof(A), typeof(B), typeof(C) }, o.TopologicalOrder);
        Assert.Equal(new[] { typeof(C), typeof(B), typeof(A) }, o.ShutdownOrder);
    }

    [Fact]
    public void CircularDependency_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Build(new[]
        {
            Desc<A>(typeof(B)), Desc<B>(typeof(A)),
        }));
    }

    [Fact]
    public void Lifecycle_ConfigureThenInitInOrder_ShutdownReversed()
    {
        var calls = new List<string>();
        var registry = Registry(calls);
        var o = new ModuleOrchestrator(new[] { Desc<A>(), Desc<B>(typeof(A)), Desc<C>(typeof(B)) }, t => registry[t]);
        var ctx = Ctx();

        o.ConfigureServicesAll(ctx);
        o.OnInitializationAll(ctx);
        o.OnShutdownAll(ctx);

        Assert.Equal("a.cs,b.cs,c.cs,a.init,b.init,c.init,c.sd,b.sd,a.sd", string.Join(',', calls));
    }

    [Fact]
    public void OptionalFailure_DegradesAndCascadesToDependents()
    {
        var calls = new List<string>();
        var registry = Registry(calls);
        registry[typeof(B)] = new FailingModule("b", calls);
        var o = new ModuleOrchestrator(new[]
        {
            Desc<A>(), Desc<B>(typeof(A), optional: true), Desc<C>(typeof(B), optional: true), Desc<D>(),
        }, t => registry[t]);
        var ctx = Ctx();

        o.ConfigureServicesAll(ctx);
        o.OnInitializationAll(ctx);
        o.OnShutdownAll(ctx);

        Assert.True(o.FailedModules.ContainsKey("b"));
        Assert.True(o.FailedModules.ContainsKey("c"));
        Assert.Contains("dependency", o.FailedModules["c"]);
        Assert.Contains("a.init", calls);
        Assert.Contains("d.init", calls);
        Assert.DoesNotContain("b.sd", calls);
    }

    [Fact]
    public void CriticalFailure_Propagates()
    {
        var calls = new List<string>();
        var registry = Registry(calls);
        registry[typeof(A)] = new FailingModule("a", calls);
        var o = new ModuleOrchestrator(new[] { Desc<A>() }, t => registry[t]);

        Assert.Throws<InvalidOperationException>(() => o.ConfigureServicesAll(Ctx()));
    }

    [Fact]
    public void TryReinit_RevivesOptionalFailedModule()
    {
        var calls = new List<string>();
        var registry = Registry(calls);
        var flaky = new FlakyModule("b", calls);
        registry[typeof(B)] = flaky;
        var o = new ModuleOrchestrator(new[] { Desc<A>(), Desc<B>(typeof(A), optional: true) }, t => registry[t]);
        var ctx = Ctx();

        o.ConfigureServicesAll(ctx);
        o.OnInitializationAll(ctx);
        Assert.True(o.FailedModules.ContainsKey("b"));

        flaky.Healthy = true;
        Assert.True(o.TryReinitModule("b", ctx, out var error));
        Assert.Null(error);
        Assert.Empty(o.FailedModules);
    }

    [Fact]
    public void TryReinit_RejectsCriticalAndHealthyModules()
    {
        var calls = new List<string>();
        var o = new ModuleOrchestrator(new[] { Desc<A>() }, t => Registry(calls)[t]);

        Assert.False(o.TryReinitModule("missing", Ctx(), out _));
        Assert.False(o.TryReinitModule("a", Ctx(), out var err));
        Assert.Contains("critical", err);
    }

    [Fact]
    public void FromType_ReadsDependsOnAttributeAndName()
    {
        var desc = ModuleDescriptor.FromType(typeof(Annotated), optional: true);

        Assert.Equal(nameof(Annotated), desc.Name);
        Assert.True(desc.Optional);
        Assert.Equal(new[] { typeof(A), typeof(B) }, desc.Dependencies);

        var named = ModuleDescriptor.FromType(typeof(Annotated), "custom");
        Assert.Equal("custom", named.Name);
    }

    [Fact]
    public void FromType_TopologicalOrderUsesAttributeDependencies()
    {
        var o = Build(new[] { Desc<A>(), Desc<B>(), ModuleDescriptor.FromType(typeof(Annotated)) });

        Assert.Equal(new[] { typeof(A), typeof(B), typeof(Annotated) }, o.TopologicalOrder);
    }

    [Fact]
    public void DuplicateName_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => Build(new[]
        {
            new ModuleDescriptor("dup", typeof(A)),
            new ModuleDescriptor("dup", typeof(B)),
        }));
        Assert.Contains("dup", ex.Message);
    }

    [Fact]
    public void OptionalShutdownFailure_DoesNotAbortRemainingShutdowns()
    {
        var calls = new List<string>();
        var registry = Registry(calls);
        registry[typeof(B)] = new ShutdownFailingModule("b", calls);
        var o = new ModuleOrchestrator(new[]
        {
            Desc<A>(), Desc<B>(typeof(A), optional: true), Desc<C>(),
        }, t => registry[t]);
        var ctx = Ctx();

        o.ConfigureServicesAll(ctx);
        o.OnInitializationAll(ctx);
        o.OnShutdownAll(ctx);

        Assert.Equal("c.sd,a.sd", string.Join(',', calls.Where(c => c.EndsWith(".sd"))));
        Assert.True(o.FailedModules.ContainsKey("b"));
    }

    private static ModuleContext Ctx() => new() { Services = new ServiceCollection() };

    private static ModuleDescriptor Desc<T>(Type? dep = null, bool optional = false)
        => new(typeof(T).Name.ToLowerInvariant(), typeof(T), dep is null ? null : new[] { dep }, optional);

    private static ModuleOrchestrator Build(ModuleDescriptor[] ds)
        => new(ds, t => (IPubSoftModule)Activator.CreateInstance(t)!);

    private static Dictionary<Type, IPubSoftModule> Registry(List<string> calls) => new()
    {
        [typeof(A)] = new Recording("a", calls),
        [typeof(B)] = new Recording("b", calls),
        [typeof(C)] = new Recording("c", calls),
        [typeof(D)] = new Recording("d", calls),
    };

    private sealed class A : Recording { public A() : base("a", new List<string>()) { } }
    private sealed class B : Recording { public B() : base("b", new List<string>()) { } }
    private sealed class C : Recording { public C() : base("c", new List<string>()) { } }
    private sealed class D : Recording { public D() : base("d", new List<string>()) { } }

    private class Recording : IPubSoftModule
    {
        private readonly string _n;
        private readonly List<string> _calls;
        public Recording(string n, List<string> calls) { _n = n; _calls = calls; }
        public virtual void ConfigureServices(ModuleContext ctx) => _calls.Add(_n + ".cs");
        public virtual void OnInitialization(ModuleContext ctx) => _calls.Add(_n + ".init");
        public virtual void OnShutdown(ModuleContext ctx) => _calls.Add(_n + ".sd");
    }

    private sealed class FailingModule : Recording
    {
        public FailingModule(string n, List<string> calls) : base(n, calls) { }
        public override void ConfigureServices(ModuleContext ctx) => throw new InvalidOperationException("boom");
    }

    private sealed class FlakyModule : Recording
    {
        public bool Healthy { get; set; }
        public FlakyModule(string n, List<string> calls) : base(n, calls) { }
        public override void OnInitialization(ModuleContext ctx)
        {
            if (!Healthy) throw new InvalidOperationException("flaky");
            base.OnInitialization(ctx);
        }
    }

    private sealed class ShutdownFailingModule : Recording
    {
        public ShutdownFailingModule(string n, List<string> calls) : base(n, calls) { }
        public override void OnShutdown(ModuleContext ctx) => throw new InvalidOperationException("sd boom");
    }

    [DependsOn(typeof(A), typeof(B))]
    private sealed class Annotated : Recording
    {
        public Annotated() : base("annotated", new List<string>()) { }
    }
}
