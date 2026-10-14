using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.DependencyInjection;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class ConventionalServiceCollectionTests
{
    public interface IFoo { }
    public sealed class Foo : IFoo, ITransientDependency { }
    public sealed class ScopedSvc : IScopedDependency { }
    public sealed class SingletonSvc : ISingletonDependency { }
    public sealed class Unmarked { }
    public abstract class AbstractMarked : ITransientDependency { }
    public sealed class MultiMarked : ITransientDependency, ISingletonDependency { }

    [Fact]
    public void RegistersMarkedTypesWithDeclaredLifetime()
    {
        var services = new ServiceCollection();
        services.AddConventionalServices(Assembly.GetExecutingAssembly());
        using var sp = services.BuildServiceProvider();

        Assert.IsType<Foo>(sp.GetRequiredService<IFoo>());
        Assert.IsType<Foo>(sp.GetRequiredService<Foo>());
        Assert.IsType<SingletonSvc>(sp.GetRequiredService<SingletonSvc>());
        Assert.Same(sp.GetRequiredService<SingletonSvc>(), sp.GetRequiredService<SingletonSvc>());
    }

    [Fact]
    public void SkipsUnmarkedAbstractAndMultiMarkedTypes()
    {
        var services = new ServiceCollection();
        services.AddConventionalServices(Assembly.GetExecutingAssembly());
        using var sp = services.BuildServiceProvider();

        Assert.Null(sp.GetService<Unmarked>());
        Assert.Null(sp.GetService<AbstractMarked>());
        Assert.Null(sp.GetService<MultiMarked>()); // 双标记不明确 — 不静默放行
    }
}
