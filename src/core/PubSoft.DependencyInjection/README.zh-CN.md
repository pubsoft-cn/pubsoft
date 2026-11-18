---
purpose: PubSoft.DependencyInjection 包级接入指南
audience: 集成方宿主开发者
owner: PubSoft 维护组
last-reviewed: 2026-10-17
---

# PubSoft.DependencyInjection

[English](README.md) | **简体中文**

约定式服务注册——通过显式标记接口扫描进 `IServiceCollection`，免掉逐类 `services.AddTransient<IFoo, Foo>()` 样板代码，不引入容器。

## 标记接口

```csharp
public interface ITransientDependency;
public interface IScopedDependency;
public interface ISingletonDependency;
```

类通过实现标记接口选择生命周期：

```csharp
public sealed class PricingService : ITransientDependency { }
```

## `AddPubSoftIdGenerator`

```csharp
services.AddPubSoftIdGenerator(workerId);            // 显式 0-31 分片
services.AddPubSoftIdGenerator(o => o.WorkerId = w); // options 委托
services.AddPubSoftIdGenerator(configuration);        // 绑定 PubSoft:IdGenerator:WorkerId
```

注册共享 `SnowflakeIdGenerator` 为 `IIdGenerator`——EF 审计持久化与消息 Outbox 的硬前置（缺失即 fail-closed）。`workerId` 必须每副本唯一；config 重载在键缺失时抛错而非猜测分片。宿主先注册自有 `IIdGenerator` 时 `TryAdd` 让位。

## 扫描注册

```csharp
services.AddConventionalServices(Assembly.GetExecutingAssembly());

// 或收窄候选集
services.AddConventionalServices(
    t => t.Namespace?.StartsWith("MyApp.Services") == true,
    typeof(Anchor).Assembly);
```

只有**可见的非抽象类**且带标记接口才会被注册（`file` 局部类型会被跳过）。每个类型注册到它实现的所有非标记 public 接口以及自身类型。无标记的类一律不注册——约定注册严格 opt-in，不改变可见性语义。

## 启动校验

工厂 lambda 注册的依赖（如 `AddHttpMessageHandler(services => ...)`）在首次使用时才解析，`ValidateOnBuild` 够不到——注册缺失会在首个请求而非启动期爆炸。`AddPubSoftStartupCheck` 登记一条具名预检查；`AddPubSoftStartupValidation` 在宿主启动时于服务作用域内运行全部检查：

```csharp
services.AddPubSoftStartupCheck("my-client",
    sp => _ = sp.GetRequiredService<IMyDependency>());
services.AddPubSoftStartupValidation();
```

失败检查聚合后默认中止启动（`StartupValidationFailureMode.Fail`）；灰度期可设 `FailureMode = Warn` 改记录日志放行。PubSoft 自带 `AddTokenHandler` 会自动登记检查——未调用 `AddPubSoftStartupValidation` 时检查处于惰性。`ValidateDetail`（`OptionsBuilder` 扩展）接受 `Func<TOptions, string?>`，让能说清原因的校验器把字段级错误带进 `OptionsValidationException`，而不是静态的"options are invalid"。

`GetNonEmpty(key)` 读取 `IConfiguration` 值，缺失/空串/空白一律视为缺失——`config["key"] ?? fallback` 会静默接受 `""`，使其通过"已配置"检查后在使用期才失败。
