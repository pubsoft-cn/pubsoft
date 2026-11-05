---
purpose: PubSoft.Events 包级接入指南
audience: 集成方宿主开发者
owner: PubSoft 维护组
last-reviewed: 2026-10-13
---

# PubSoft.Events

[English](README.md) | **简体中文**

`PubSoft.Events` 提供进程内领域事件分发：await 的同步分发 + 有界后台队列。不持久化事件、不跨进程协调、重启后不恢复队列事件——需要与数据库变更一起持久提交时用 `PubSoft.Messaging.Outbox`。

## 注册

注册包与 handler（按其需要的生命周期）；dispatcher 为单例，每次分发创建 DI scope——handler 可为 scoped：

```csharp
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Events;

services.AddPubSoftEvents(options => options.Capacity = 2_000);
services.AddScoped<IDomainEventHandler<OrderSubmitted>, UpdateOrderProjection>();
```

`Capacity` 取值 1–100,000，默认 1,000。`AddPubSoftEvents` 同时注册后台 hosted service，宿主启动后 `PublishAsync` 即开始消费。

事件载荷建议使用不可变对象——队列在进程内传递对象引用，不序列化或快照。

## 分发模式

调用方需等待 handler 完成时用 `DispatchAsync`：

```csharp
await dispatcher.DispatchAsync(new OrderSubmitted(orderId), cancellationToken);
```

`RegisterHandler<TEvent>` 手动注册的 handler 先执行，其次为 DI 注册的精确事件类型 handler（按注册顺序）。handler 异常会传播并中断剩余同步分发；调用方取消同样传播。

后台处理用 `PublishAsync`：

```csharp
await dispatcher.PublishAsync(new OrderSubmitted(orderId), cancellationToken);
```

队列单 reader 顺序处理；容量满时发布方等待空位并响应取消——不静默丢弃。`PublishAsync` 返回仅表示事件已进入内存队列，不代表 handler 完成。handler 异常记录日志并隔离，后续 handler/事件继续；宿主关闭期间的取消会停止 reader，队列内事件不保证完成。

## 投递边界

本 dispatcher 是单进程内的 best-effort——不提供持久投递、重试、分布式顺序或跨实例去重。不得作为必须跨进程故障存活工作的唯一记录。可靠投递请在业务事务的同 DbContext 中 staging Outbox 消息并由 Outbox worker 分发。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Events`: `DomainEvent` `DomainEventBackgroundService` `DomainEventDispatcher` `DomainEventOptions` `DomainEventServiceCollectionExtensions` `IDomainEvent` `IDomainEventDispatcher` `IDomainEventHandler<TEvent>`

## 验证

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~DomainEventsTests
bash scripts/verify-package-consumer.sh
```
