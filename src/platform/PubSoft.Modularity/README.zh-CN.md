---
purpose: PubSoft.Modularity 包级接入指南
audience: 集成方宿主开发者
owner: PubSoft 维护组
last-reviewed: 2026-11-03
---

# PubSoft.Modularity

声明式模块编排——宿主以模块而非平铺 `Program.cs` 组合特性。提供 ABP 式生命周期但不依赖 ABP。

```csharp
var orchestrator = new ModuleOrchestrator(
    new[]
    {
        new ModuleDescriptor("core", typeof(CoreModule)),
        new ModuleDescriptor("search", typeof(SearchModule), new[] { typeof(CoreModule) }, Optional: true),
    },
    t => t == typeof(CoreModule)           // 显式工厂——PublishAot 下安全
        ? (IPubSoftModule)new CoreModule()
        : new SearchModule());
var ctx = new ModuleContext { Services = services };

orchestrator.ConfigureServicesAll(ctx);   // DI 注册，按拓扑序
orchestrator.OnInitializationAll(ctx);    // 同序，可设 ServiceProvider
orchestrator.OnShutdownAll(ctx);          // 逆序
```

- 拓扑序来自 `ModuleDescriptor.Dependencies`（或 `[DependsOn]` + `ModuleDescriptor.FromType`）；循环依赖立即抛 `InvalidOperationException`。
- `Optional: true` 模块在任一阶段抛错会记入 `FailedModules` 并跳过——依赖它的模块级联跳过而非自身报错。关键（非可选）模块失败照常抛出。
- `TryReinitModule(name, ctx, out error)` 就地重启降级的可选模块（先关残留→配置→初始化）并级联复活依赖方；关键模块须进程级重启。注意重跑 `ConfigureServices` 写入的是启动时捕获的同一个 `IServiceCollection`——`BuildServiceProvider` 之后这些注册不再生效，因此 reinit 实际只重放 `OnInitialization`（外加释放关停残留）。它是生命周期重试，不是服务重注册。
- `ModuleContext` 携带 `IServiceCollection`、可选 `IServiceProvider`（初始化/关闭阶段前设置）、可选 `IConfiguration`（`context.GetConfiguration()` 先读它、再解析 provider 的 `IConfiguration`）、可选 `ILogger`，以及可选 `ShutdownToken`——供宿主在关停时传达优雅停止时限。
- `OnInitialization` 与 `OnShutdown` 提供默认空实现——只关心 `ConfigureServices` 的模块无需再写空桩。`OnInitializationAsync`/`OnShutdownAsync`（`ValueTask`）默认桥接 sync 钩子——优先用它而非 `async void` 覆盖：后者首个 await 之后的异常会逃逸编排器的可选模块降级过滤、直接崩进程。编排器 async 入口（`OnInitializationAllAsync`、`OnShutdownAllAsync`、`TryReinitModuleAsync`）会 await 它们；sync `*All` 包装阻塞在 async 路径上。
- 在 `PublishAot` 下请传入显式工厂（如上）或从容器解析——`Activator.CreateInstance(Type)` 基于反射，会产生 RUC 警告。

不做程序集扫描、不自造 DI 容器——模块由描述符数组显式枚举，符合库的显式 opt-in 原则。

## 宿主胶水

`AddPubSoftModules` 把编排器构建 + `ConfigureServicesAll` 收编进注册期；`UsePubSoftModulesAsync`（`PubSoft.AspNetCore`）或 `StartPubSoftModulesAsync`（`IHost`）跑初始化并把关停挂到 `ApplicationStopping`：

```csharp
builder.Services.AddPubSoftModules(builder.Configuration, modules =>
    modules.Add<CoreModule>().Add<SearchModule>(optional: true));
// web 宿主：
await app.UsePubSoftModulesAsync();          // 模块 init 可读 ctx.GetAppBuilder()/GetEnv()
// 通用宿主：await host.StartPubSoftModulesAsync();
```

`Add<T>` 在 `ConfigureServices` 阶段无容器激活（需无参 ctor），provider 就绪后走 `ActivatorUtilities`（public ctor 已做裁剪标注，不产生 RUC 警告）；`Add(descriptor, factory)` 是全显式路径。web 模块经 `WebModuleContext`/`ctx.GetAppBuilder()` 拿管线；`ctx.GetEnv()` 从 provider 解析 `IHostEnvironment`。
