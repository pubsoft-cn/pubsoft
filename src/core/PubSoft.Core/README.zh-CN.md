---
purpose: PubSoft.Core 包级接入指南
audience: 集成方宿主开发者
owner: PubSoft 维护组
last-reviewed: 2026-10-13
---

# PubSoft.Core

[English](README.md) | **简体中文**

`PubSoft.Core` 提供框架无关的值类型与通用辅助件。不定义 HTTP 响应处理、持久化或业务专属校验策略。

## Result 值

当操作的预期失败应以数据形式返回而非抛异常时，使用 `Result<T>`：

```csharp
using PubSoft.Core;

Result<int> parsed = int.TryParse(input, out var value)
    ? Result<int>.Ok(value)
    : Result<int>.Fail("invalid_number", "The value is not an integer.");

Result<string> label = parsed.Map(number => $"Item {number}");
```

`Map`/`Bind` 传播已有错误并拒绝 null selector；selector 抛出的异常不会转换为失败。对失败的 Result 访问 `Value` 抛 `InvalidOperationException`——检查 `IsOk` 或用 `Unwrap` 提供回退。错误码与消息由调用方定义，不自动本地化或脱敏。

## Money

`Money` 以有符号 `long` 存储最小货币单位（`Fen`），避免二进制浮点运算：

```csharp
var amount = Money.FromYuan(12.34m);
Money fee = amount.AllocatePercent(2.5m);
decimal yuan = amount.ToYuan();
```

`FromYuan`/`AllocatePercent` 中点舍入为远离零；`AllocateBps` 向零截断小数单位，不校验业务基点范围。加减、一元负号、整数乘法均为 checked，越界抛 `OverflowException`。币种与分摊余数策略不在本类型职责内。

## Guard 与 Specification

`Guard` 提供小型抛异常检查。`NotEmpty` 拒绝 null/空/纯空白但返回原字符串不做 trim；`Phone` 只校验最多 16 位数字，不校验国家码格式。失败抛 `DomainException`。

继承 `Specification<T>` 收集条件、include、排序、分组与可选分页供仓储/查询适配器消费。`ApplyPaging` 在改变分页状态前拒绝负 skip/take（零有效）；specification 只记录表达式，不执行或翻译——provider 翻译与查询上限归消费适配器。

## ID 与响应形状

`SnowflakeIdGenerator` 生成 JS 安全 53-bit ID：2024-01-01 UTC epoch + 5-bit worker ID（0–31）+ 7-bit 每毫秒序列。注入 `TimeProvider` 可做确定性测试。时钟不得回拨，时间戳不得超出支持窗口。共享同一 ID 空间的并发生成器须分配不同 worker ID——实例内锁只协调单实例调用。最后一个支持毫秒耗尽序列号空间（每毫秒 128）时抛异常而非回绕。

DI 注册是宿主侧一行：`services.AddSingleton(new SnowflakeIdGenerator(workerId))`——构造前校验 worker ID（0–31，并发生成器间唯一）；需要 options 绑定 + 启动校验的宿主可自行包 `AddOptions<T>().Validate(...)`。

`ApiResponse<T>`/`ApiResponse` 是可变响应 DTO：code `0` 表示成功，`Fail` 拒绝 code `0`。不设置 HTTP 状态码、不定义全局错误码注册表、不强制序列化约定。`PagedResultDto<T>` 携带可变列表与总数，不规定分页语义。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Core`: `ApiResponse` `ApiResponse<T>` `DomainException` `Gender` `Guard` `ISpecification<T>` `Money` `PagedResultDto<T>` `Result<T>` `ResultError` `SnowflakeIdGenerator` `Specification<T>`
