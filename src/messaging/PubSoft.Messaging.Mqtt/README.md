---
purpose: package-level hosting guide for PubSoft.Messaging.Mqtt
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-11-04
---

# PubSoft.Messaging.Mqtt

MQTT transport for the PubSoft transactional outbox — `MqttOutboxDispatcher` publishes
`OutboxMessage` payloads to `{TopicPrefix}{EventType}`; `MqttInboxSubscriber` consumes
them through `IInboxMessageProcessor` with `InboxReceipt` dedup.

## Broker requirements

**MQTT 5.0 required.** The following v5 features are load-bearing:

| Feature | Used for |
|---|---|
| User Properties | `x-outbox-id` / `x-outbox-aggkey` headers — consumer-side `InboxReceipt` dedup key |
| Session Expiry Interval | offline-queue durability for `PersistentSession` (`MqttInboxOptions.SessionExpirySeconds`) |
| Reason codes | publish failures surface as typed errors (`PacketTooLarge`, `NotAuthorized`) |

MQTT 3.1.1 brokers lose the dedup identity — `x-outbox-id` rides as a v5 user
property that v3.1.1 silently drops. On a v3-only broker, `MqttInboxSubscriber`
discards received messages (no dedup key). The `MqttOutboxDispatcher` alone
still works on v3.1.1.

## Optional broker capabilities

| Capability | Behavior when absent |
|---|---|
| Shared subscriptions (`$share/{group}/{filter}`) | Set `MqttInboxOptions.ShareGroup = null` — falls back to a normal subscription (each instance receives every message; dedup still correct via InboxReceipt) |
| Offline message queue | Without a persistent-session-capable broker, messages published while the subscriber is down are lost — `IOutboxStore` retains them until `MarkDelivered`, so the publisher may redeliver per outbox retry policy |
| Payload size > 64KB | `MqttOutboxOptions.MaxPayloadBytes` client-side pre-check rejects with a claim-check hint before the broker is involved |

## Configuration

```csharp
services.AddPubSoftMqttOutboxDispatcher(o =>
{
    o.BrokerUri = "mqtt://broker:1883";   // mqtts:// for TLS
    o.ClientId = "order-svc-out";
    o.Username = cfg["Mqtt:User"];        // credentials via config/env, never in source
    o.Password = cfg["Mqtt:Pass"];
    o.TopicPrefix = "ext/outbox/";
});

services.AddHostedService(sp => new MqttInboxSubscriber(
    new MqttInboxOptions
    {
        BrokerUri = "mqtt://broker:1883",
        Username = cfg["Mqtt:User"], Password = cfg["Mqtt:Pass"],
        TopicFilter = "ext/outbox/#",
        ShareGroup = "outbox-workers",      // EMQX-style shared subscription; null if unsupported
        EventTypePrefix = "ext/outbox/",
        ConsumerName = "order-svc",         // InboxReceipt dedup identity — per logical consumer
        PersistentSession = true,           // + SessionExpirySeconds (default 24h) for offline durability
    },
    inboxProcessor,
    (eventType, payloadJson, ct) => HandleEventAsync(eventType, payloadJson, ct)));
```

Delivery semantics: QoS1 at-least-once + `InboxReceipt` exactly-once-effective.
No offset/replay — for durable event log with replay use a dedicated broker
(Kafka / NATS JetStream).
