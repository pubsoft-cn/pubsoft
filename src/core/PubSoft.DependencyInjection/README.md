---
purpose: package-level hosting guide for PubSoft.DependencyInjection
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-17
---

# PubSoft.DependencyInjection

**English** | [简体中文](https://github.com/pubsoft-cn/pubsoft/blob/main/src/core/PubSoft.DependencyInjection/README.zh-CN.md)

Conventional service registration — opt-in marker interfaces scanned into `IServiceCollection`, removing per-class `services.AddTransient<IFoo, Foo>()` boilerplate without a container.

## Marker Interfaces

```csharp
public interface ITransientDependency;
public interface IScopedDependency;
public interface ISingletonDependency;
```

A class picks its lifetime by implementing a marker:

```csharp
public sealed class PricingService : ITransientDependency { }
```

## Scanning

```csharp
services.AddConventionalServices(Assembly.GetExecutingAssembly());

// or narrow the candidate set
services.AddConventionalServices(
    t => t.Namespace?.StartsWith("MyApp.Services") == true,
    typeof(Anchor).Assembly);
```

Only **visible, non-abstract classes** carrying a marker are registered (file-local types are skipped). Each type is registered under every non-marker public interface it implements, plus the concrete type itself. Classes without a marker are never registered — conventional registration is strictly opt-in and does not change visibility semantics.
