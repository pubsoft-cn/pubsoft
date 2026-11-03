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
- `ModuleContext` 携带 `IServiceCollection`、可选 `IServiceProvider`（初始化/关闭阶段前设置）、可选 `ILogger`，以及可选 `ShutdownToken`——供宿主在关停时传达优雅停止时限。
- `OnInitialization` 与 `OnShutdown` 提供默认空实现——只关心 `ConfigureServices` 的模块无需再写空桩。
- 在 `PublishAot` 下请传入显式工厂（如上）或从容器解析——`Activator.CreateInstance(Type)` 基于反射，会产生 RUC 警告。

不做程序集扫描、不自造 DI 容器——模块由描述符数组显式枚举，符合库的显式 opt-in 原则。
