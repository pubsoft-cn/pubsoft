# PubSoft.Messaging

[English](README.md) | **简体中文**

`PubSoft.Messaging` 定义 transport 中立的发布者契约、JSON 消息 envelope 与显式 handler dispatcher。不提供 broker、持久化、投递保证、重试或去重——需随数据库变更事务化 staging 时用 `PubSoft.Messaging.Outbox`；transport 适配器实现 `IMessagePublisher`。

## Envelope

使用稳定、带版本的消息键——独立于 CLR 类型与程序集名：

```csharp
using PubSoft.Messaging;

var envelope = MessageEnvelope.Create(
    messageId: Guid.NewGuid().ToString("N"),
    messageType: "orders.order-created.v1",
    payload: new OrderCreated(orderId, customerId),
    occurredAt: DateTimeOffset.UtcNow,
    source: "orders",
    target: "billing");
```

envelope 存储不透明消息 ID、显式消息类型键、克隆的 `JsonElement` payload、发生时间与可选 source/target 标签。ID 上限 128 个非控制字符；键与标签上限 256。payload JSON 不用于推断运行时类型。发布方与消费方须独立于包 transport 约定键、payload schema 与 `JsonSerializerOptions`。

## Handler 注册与分发

每个键显式注册到一种 payload 类型与一个或多个 handler：

```csharp
var dispatcher = new MessageDispatcher();
dispatcher.Register("orders.order-created.v1", new BillingOrderCreatedHandler());
await dispatcher.DispatchAsync(envelope, cancellationToken);
```

消息键为序数、区分大小写。同键多 handler 按注册顺序执行；同键注册不同 payload 类型立即抛异常。未知键以 `InvalidOperationException` fail closed；畸形/null payload 拒绝分发而非动态解析。分发前检查取消并传递给每个 handler。

`MessageDispatcher` 在自身生命周期内持有 handler 实例——注册线程安全且依赖生命周期匹配的实例，或由 transport 侧 scoped dispatch 层在合适 scope 内构造 dispatcher/handler。本包不提供 DI 注册或 handler 发现。

## 发布者契约

`IMessagePublisher.PublishAsync(topic, envelope, cancellationToken)` 是唯一的 transport 契约——其实现定义 topic 校验、持久化、重试、顺序与投递语义；除非该 transport 明确保证，否则调用方不得假设成功返回即已被消费。可能重复投递时使用不透明 ID 与应用级幂等。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Messaging`: `IMessageHandler<TMessage>` `IMessagePublisher` `MessageDispatcher` `MessageEnvelope`

## 验证

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~MessagingContractTests
bash scripts/verify-package-consumer.sh
```
