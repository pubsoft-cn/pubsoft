# PubSoft.Mapping

[English](README.md) | **简体中文**

`PubSoft.Mapping` 是零依赖对象映射器，编译并缓存表达式委托。不扫描程序集、不自动发现映射——名称匹配不够时在启动期显式注册配置。

## 自动映射

按精确名称匹配公共源/目标属性；支持双向 `Ms` 后缀约定：目标 `CreatedAtMs` 可读源 `CreatedAt`，目标 `CreatedAt` 可读源 `CreatedAtMs`。

```csharp
var dto = ObjectMapper.Map<Order, OrderDto>(order);
var dtos = ObjectMapper.MapList<Order, OrderDto>(orders);
```

`MapList` 只枚举一次源，引用类型目标保留 null 元素；`Map` 收到 null 源时返回目标默认值。

## 显式配置

映射前注册目标表达式、常量或忽略属性：

```csharp
ObjectMapper.Register<Order, OrderDto>(mapping => mapping
    .Map(destination => destination.DisplayName, source => source.Customer.Name)
    .Const(destination => destination.Currency, "USD")
    .Ignore(destination => destination.InternalNotes));
```

selector 必须直接指向公共可写目标属性，无效 selector 在注册期失败。同类型对重复注册会替换已编译映射——配置请在启动期完成，不要与请求并发竞争。

## 类型转换器

首次映射该类型对之前注册转换器：

```csharp
ObjectMapper.Convert<Money, string>(money => money.ToString());
```

转换器为进程级静态注册。源/目标类型不同且无内置转换时按属性查询转换器。内置转换：可赋值类型、nullable 包/解包（null nullable 映射为目标默认值）、表达式支持数值/枚举转换、`int`→`bool`、invariant `long`→`string`。不支持的转换在编译映射时失败——补转换器或显式源表达式。

## 运维注意

映射委托与转换器按类型对静态缓存。避免请求级配置，不要在转换器中捕获 scoped 服务。映射器新建目标实例并映射公共可写属性——不是深拷贝框架，不递归映射嵌套对象（除非显式表达式/转换器如此做）。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Mapping`: `MappingBuilder<TSource, TDestination>` `ObjectMapper`

## 验证

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~MappingTests
bash scripts/verify-package-consumer.sh
```
