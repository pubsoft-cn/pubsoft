---
purpose: PubSoft.Messaging.Amqp 包级宿主接入指南
audience: 接入方宿主开发者
owner: PubSoft maintainers
last-reviewed: 2026-11-18
---

# PubSoft.Messaging.Amqp

PubSoft 事务性 outbox 的 AMQP 0-9-1（RabbitMQ）传输实现——
`AmqpOutboxDispatcher` 将 `OutboxMessage` 负载发布到声明为 durable 的
exchange，routing key 为 `{RoutingKeyPrefix}{EventType}`；broker 以
publisher confirm 确认接收后，outbox 行才标记为已投递。

## Broker 要求

已验证 RabbitMQ 4.x。dispatcher 依赖：

| 特性 | 用途 |
|---|---|
| Publisher confirms | at-least-once 投递——`DispatchAsync` 在 broker ack 前不返回 |
| `mandatory` 标志 + basic.return | 不可路由的 routing key 使投递失败，而不是静默丢消息 |
| AMQP headers | `x-outbox-id` / `x-outbox-aggkey` / `x-outbox-correlation-id`——消费端 `InboxReceipt` 去重键与链路关联 |

投递语义：publisher-confirm at-least-once + 消费端 `InboxReceipt` 等效
exactly-once。消息 header 值在客户端呈现为 `byte[]`（AMQP `longstr`）——
按 UTF-8 解码。

## 配置

```csharp
services.AddPubSoftAmqpOutboxDispatcher(o =>
{
    o.BrokerUri = "amqp://user:pass@broker:5672/vhost";   // TLS 用 amqps://
    o.Exchange = "pubsoft.outbox";                        // 声明为 durable topic
    o.RoutingKeyPrefix = "outbox.";                       // key = 前缀 + EventType
});
```

## 投递保障与边界

| 保障 | 机制 |
|---|---|
| broker 已接收消息 | publisher confirms；失败抛异常，outbox 重试 |
| 不可路由消息不静默丢弃 | `mandatory` return → `InvalidOperationException`；越过 confirm 窗口的 return 记日志 |
| 消费端去重 | `x-outbox-id` header——与 MQTT 传输同一契约（`PubSoft.Messaging.Outbox.OutboxHeaders`） |

宿主保留职责：队列拓扑与绑定（dispatcher 只声明自身 exchange）、vhost 与
凭据保管、消费端 inbox 处理、broker 侧保留策略。

## API surface

<!-- pubsoft:api-surface:start -->

机器维护的公开 API 面（重新生成：`dotnet run --project tools/PubSoft.DocsTool -- --sync`）。

<!-- pubsoft:api-surface:end -->
