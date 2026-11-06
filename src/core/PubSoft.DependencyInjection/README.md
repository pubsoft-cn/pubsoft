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

## `AddPubSoftIdGenerator`

```csharp
services.AddPubSoftIdGenerator(workerId);          // explicit 0-31 shard
services.AddPubSoftIdGenerator(o => o.WorkerId = w); // options delegate
services.AddPubSoftIdGenerator(configuration);      // binds PubSoft:IdGenerator:WorkerId
```

Registers one shared `SnowflakeIdGenerator` as `IIdGenerator` — required by EF audit persistence and the messaging outbox (both fail closed when it is absent). `workerId` must be unique per live replica; the config overload throws when the key is missing rather than guessing a shard. `TryAdd` yields when the host registers its own `IIdGenerator` first.

## Scanning

```csharp
services.AddConventionalServices(Assembly.GetExecutingAssembly());

// or narrow the candidate set
services.AddConventionalServices(
    t => t.Namespace?.StartsWith("MyApp.Services") == true,
    typeof(Anchor).Assembly);
```

Only **visible, non-abstract classes** carrying a marker are registered (file-local types are skipped). Each type is registered under every non-marker public interface it implements, plus the concrete type itself. Classes without a marker are never registered — conventional registration is strictly opt-in and does not change visibility semantics.
