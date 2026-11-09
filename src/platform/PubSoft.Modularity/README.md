---
purpose: package-level hosting guide for PubSoft.Modularity
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-11-03
---

# PubSoft.Modularity

Declarative module orchestration for hosts that compose features as modules rather than a flat `Program.cs`. Optional ABP-style lifecycle without the ABP dependency.

```csharp
var orchestrator = new ModuleOrchestrator(
    new[]
    {
        new ModuleDescriptor("core", typeof(CoreModule)),
        new ModuleDescriptor("search", typeof(SearchModule), new[] { typeof(CoreModule) }, Optional: true),
    },
    t => t == typeof(CoreModule)           // explicit factory — safe under PublishAot
        ? (IPubSoftModule)new CoreModule()
        : new SearchModule());
var ctx = new ModuleContext { Services = services };

orchestrator.ConfigureServicesAll(ctx);   // DI registration, topological order
orchestrator.OnInitializationAll(ctx);    // same order, ServiceProvider available
orchestrator.OnShutdownAll(ctx);          // reverse order
```

- Topological order comes from `ModuleDescriptor.Dependencies` (or `[DependsOn]` + `ModuleDescriptor.FromType`); cycles fail fast with `InvalidOperationException`.
- `Optional: true` modules that throw during a phase are marked in `FailedModules` and skipped — dependents cascade-skip rather than fail themselves. Critical (non-optional) failures propagate.
- `TryReinitModule(name, ctx, out error)` restarts a degraded optional module in place (shutdown residuals → configure → initialize) and cascades revival to dependents; critical modules must be recycled by process. Note that re-running `ConfigureServices` writes into the same `IServiceCollection` captured at startup — once `BuildServiceProvider` has run those registrations are inert, so in practice reinit replays `OnInitialization` (plus releasing shutdown residuals). It is a lifecycle retry, not a service re-registration.
- `ModuleContext` carries `IServiceCollection`, an optional `IServiceProvider` (set it before the initialization/shutdown phases), an optional `IConfiguration` (`context.GetConfiguration()` resolves it or the provider's `IConfiguration`), an optional `ILogger`, and an optional `ShutdownToken` for hosts that want to signal graceful-stop deadlines during shutdown.
- `OnInitialization` and `OnShutdown` have default empty implementations — a module that only contributes `ConfigureServices` does not need stubs. `OnInitializationAsync`/`OnShutdownAsync` (`ValueTask`) bridge to the sync hooks by default; prefer them over `async void` overrides, whose post-await exceptions escape the orchestrator's optional-module degradation filter. Orchestrator async entry points (`OnInitializationAllAsync`, `OnShutdownAllAsync`, `TryReinitModuleAsync`) await them; the sync `*All` wrappers block on the async path.
- Under `PublishAot`, pass an explicit factory (as above) or resolve modules from your container — `Activator.CreateInstance(Type)` is reflection-based and produces RUC warnings.

No assembly scanning and no DI container of its own — modules are enumerated explicitly through the descriptor array, matching the library's explicit opt-in principle.

## Host glue

`AddPubSoftModules` folds orchestrator construction + `ConfigureServicesAll` into registration; `UsePubSoftModulesAsync` (`PubSoft.AspNetCore`) or `StartPubSoftModulesAsync` (`IHost`) run init and hook shutdown to `ApplicationStopping`:

```csharp
builder.Services.AddPubSoftModules(builder.Configuration, modules =>
    modules.Add<CoreModule>().Add<SearchModule>(optional: true));
// web host:
await app.UsePubSoftModulesAsync();          // module init sees ctx.GetAppBuilder()/GetEnv()
// generic host: await host.StartPubSoftModulesAsync();
```

`Add<T>` activates modules parameterless during `ConfigureServices` and via `ActivatorUtilities` once the provider exists (public ctors are trim-annotated, so no RUC warnings); `Add(descriptor, factory)` is the fully explicit path. Web modules read the pipeline through `WebModuleContext`/`ctx.GetAppBuilder()`; `ctx.GetEnv()` resolves `IHostEnvironment` from the provider.

## API surface

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**Registration**

| Method |
|---|---|
| `AddPubSoftModules` |
| `StartPubSoftModulesAsync` |

**(options & records)**

`Configuration`, `Dependencies`, `Logger`, `ModuleType`, `Optional`, `ServiceProvider`, `Services`, `ShutdownToken`

<!-- pubsoft:api-surface:end -->
