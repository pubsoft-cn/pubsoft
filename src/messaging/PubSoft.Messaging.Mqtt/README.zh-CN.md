# PubSoft.Messaging.Mqtt

PubSoft 事务性 outbox 的 MQTT 传输——`MqttOutboxDispatcher` 将 `OutboxMessage`
载荷发布到 `{TopicPrefix}{EventType}`；`MqttInboxSubscriber` 经
`IInboxMessageProcessor` 消费并以 `InboxReceipt` 去重。

## Broker 要求

**需要 MQTT 5.0。** 以下 v5 特性是承重依赖：

| 特性 | 用途 |
|---|---|
| User Properties | `x-outbox-id` / `x-outbox-aggkey` 头——消费端 `InboxReceipt` 去重键 |
| Session Expiry Interval | `PersistentSession` 的离线队列持久化（`MqttInboxOptions.SessionExpirySeconds`） |
| Reason codes | 发布失败映射为类型化错误（`PacketTooLarge`、`NotAuthorized`） |

MQTT 3.1.1 broker 会丢失去重身份——`x-outbox-id` 以 v5 user property 携带，
v3.1.1 会静默丢弃。在仅 v3 的 broker 上，`MqttInboxSubscriber` 会丢弃收到的
消息（无去重键）。单独的 `MqttOutboxDispatcher`（仅发布）在 v3.1.1 上仍可用。

## 可选 broker 能力

| 能力 | 缺失时行为 |
|---|---|
| 共享订阅（`$share/{group}/{filter}`） | `MqttInboxOptions.ShareGroup = null` 退化为普通订阅——每个实例都收到全部消息；`InboxReceipt` 去重仍保证正确性（仅多耗流量） |
| 离线消息队列 | broker 不支持持久会话时，订阅端离线期间的消息会丢——但 `IOutboxStore` 在 `MarkDelivered` 前保留消息，发送端可按 outbox 重试策略补投 |
| Payload > 64KB | `MqttOutboxOptions.MaxPayloadBytes` 客户端预检在接触 broker 前拒发，并提示走 claim-check |

## 配置

```csharp
services.AddPubSoftMqttOutboxDispatcher(o =>
{
    o.BrokerUri = "mqtt://broker:1883";   // TLS 用 mqtts://
    o.ClientId = "order-svc-out";
    o.Username = cfg["Mqtt:User"];        // 凭证走配置/环境变量，勿入库
    o.Password = cfg["Mqtt:Pass"];
    o.TopicPrefix = "ext/outbox/";
});

services.AddHostedService(sp => new MqttInboxSubscriber(
    new MqttInboxOptions
    {
        BrokerUri = "mqtt://broker:1883",
        Username = cfg["Mqtt:User"], Password = cfg["Mqtt:Pass"],
        TopicFilter = "ext/outbox/#",
        ShareGroup = "outbox-workers",      // EMQX 系共享订阅；不支持则置 null
        EventTypePrefix = "ext/outbox/",
        ConsumerName = "order-svc",         // InboxReceipt 去重身份——按逻辑消费者
        PersistentSession = true,           // + SessionExpirySeconds（默认 24h）离线持久
    },
    inboxProcessor,
    (eventType, payloadJson, ct) => HandleEventAsync(eventType, payloadJson, ct)));
```

投递语义：QoS1 at-least-once + `InboxReceipt` 等效 exactly-once。
无 offset/回放语义——需要持久事件日志与回放请用专职 MQ（Kafka / NATS JetStream）。
