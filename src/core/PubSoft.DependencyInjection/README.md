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

<!-- pubsoft:snippet:AddPubSoftIdGenerator -->
```csharp
internal static void ExplicitWorkerId(IServiceCollection services, long workerId)
    => services.AddPubSoftIdGenerator(workerId);

internal static void OptionsDelegate(IServiceCollection services, long w)
    => services.AddPubSoftIdGenerator(o => o.WorkerId = w);

internal static void FromConfiguration(IServiceCollection services, IConfiguration configuration)
    => services.AddPubSoftIdGenerator(configuration);
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

## Startup validation

Factory-lambda registrations (for example `AddHttpMessageHandler(services => ...)`) resolve their dependencies lazily at first use, so `ValidateOnBuild` never reaches them — a missing registration fails on the first request instead of at startup. `AddPubSoftStartupCheck` names an eager check; `AddPubSoftStartupValidation` runs every registered check inside a service scope once at host start:

```csharp
services.AddPubSoftStartupCheck("my-client",
    sp => _ = sp.GetRequiredService<IMyDependency>());
services.AddPubSoftStartupValidation();
```

Failed checks are aggregated and abort startup by default (`StartupValidationFailureMode.Fail`); set `FailureMode = Warn` to log and continue during staged rollouts. PubSoft's own `AddTokenHandler` registers its check automatically — the validator is inert until `AddPubSoftStartupValidation` is called. `ValidateDetail` (an `OptionsBuilder` extension) accepts a `Func<TOptions, string?>` so validators that know *why* they failed put the field-level reason into `OptionsValidationException` instead of a static "options are invalid" message.

`GetNonEmpty(key)` reads an `IConfiguration` value treating absent, empty and whitespace as missing — `config["key"] ?? fallback` silently accepts `""`, which later passes "configured" checks and fails at first use.

## API surface

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**Registration**

| Method |
|---|---|
| `AddPubSoftIdGenerator` |
| `AddPubSoftStartupCheck` |
| `AddPubSoftStartupValidation` |

**(options & records)**

`FailureMode`, `WorkerId`

<!-- pubsoft:api-surface:end -->
