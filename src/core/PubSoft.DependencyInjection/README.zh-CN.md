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

## 扫描注册

```csharp
services.AddConventionalServices(Assembly.GetExecutingAssembly());

// 或收窄候选集
services.AddConventionalServices(
    t => t.Namespace?.StartsWith("MyApp.Services") == true,
    typeof(Anchor).Assembly);
```

只有**可见的非抽象类**且带标记接口才会被注册（`file` 局部类型会被跳过）。每个类型注册到它实现的所有非标记 public 接口以及自身类型。无标记的类一律不注册——约定注册严格 opt-in，不改变可见性语义。
