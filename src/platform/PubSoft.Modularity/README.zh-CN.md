# PubSoft.Modularity

声明式模块编排——宿主以模块而非平铺 `Program.cs` 组合特性。提供 ABP 式生命周期但不依赖 ABP。

```csharp
var orchestrator = new ModuleOrchestrator(
    new[]
    {
        new ModuleDescriptor("core", typeof(CoreModule)),
        new ModuleDescriptor("search", typeof(SearchModule), new[] { typeof(CoreModule) }, Optional: true),
    },
    t => (IPubSoftModule)Activator.CreateInstance(t)!);
var ctx = new ModuleContext { Services = services };

orchestrator.ConfigureServicesAll(ctx);   // DI 注册，按拓扑序
orchestrator.OnInitializationAll(ctx);    // 同序，可设 ServiceProvider
orchestrator.OnShutdownAll(ctx);          // 逆序
```

- 拓扑序来自 `ModuleDescriptor.Dependencies`（或 `[DependsOn]` + `ModuleDescriptor.FromType`）；循环依赖立即抛 `InvalidOperationException`。
- `Optional: true` 模块在任一阶段抛错会记入 `FailedModules` 并跳过——依赖它的模块级联跳过而非自身报错。关键（非可选）模块失败照常抛出。
- `TryReinitModule(name, ctx, out error)` 就地重启降级的可选模块（先关残留→配置→初始化）并级联复活依赖方；关键模块须进程级重启。
- `ModuleContext` 携带 `IServiceCollection`、可选 `IServiceProvider`（初始化/关闭阶段前设置）与可选 `ILogger`。

不做程序集扫描、不自造 DI 容器——模块由描述符数组显式枚举，符合库的显式 opt-in 原则。
