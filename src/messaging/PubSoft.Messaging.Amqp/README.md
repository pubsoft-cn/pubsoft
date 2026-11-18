---
purpose: package-level hosting guide for PubSoft.Messaging.Amqp
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-11-18
---

# PubSoft.Messaging.Amqp

AMQP 0-9-1 (RabbitMQ) transport for the PubSoft transactional outbox —
`AmqpOutboxDispatcher` publishes `OutboxMessage` payloads to
`{RoutingKeyPrefix}{EventType}` on a declared durable exchange, with publisher
confirms acking broker acceptance before the outbox marks the row delivered.

## Broker requirements

Tested against RabbitMQ 4.x. The dispatcher relies on:

| Feature | Used for |
|---|---|
| Publisher confirms | at-least-once delivery — `DispatchAsync` only returns after the broker acks |
| `mandatory` flag + basic.return | unroutable routing keys fail the dispatch instead of silently dropping the event |
| AMQP headers | `x-outbox-id` / `x-outbox-aggkey` / `x-outbox-correlation-id` — consumer-side `InboxReceipt` dedup key and trace correlation |

Delivery semantics: publisher-confirm at-least-once + `InboxReceipt`
exactly-once-effective on the consumer side. Message header values surface as
`byte[]` (AMQP `longstr`) to consumers — decode as UTF-8.

## Configuration

```csharp
services.AddPubSoftAmqpOutboxDispatcher(o =>
{
    o.BrokerUri = "amqp://user:pass@broker:5672/vhost";   // amqps:// for TLS
    o.Exchange = "pubsoft.outbox";                        // declared durable topic
    o.RoutingKeyPrefix = "outbox.";                       // key = prefix + EventType
});
```

## Delivery guarantees and residuals

| Guarantee | Mechanism |
|---|---|
| Broker accepted the message | publisher confirms; failures throw and the outbox retries |
| Unroutable messages are not dropped silently | `mandatory` return → `InvalidOperationException`; a return racing past the confirm window is logged |
| Consumer dedup | `x-outbox-id` header — identical contract to the MQTT transport (`PubSoft.Messaging.Outbox.OutboxHeaders`) |

Residual host responsibilities: queue topology and bindings (the dispatcher
declares only its own exchange), vhost/credential custody, consumer-side inbox
processing, and broker-side retention policies.

## API surface

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**Registration**

| Method |
|---|---|
| `AddPubSoftAmqpOutboxDispatcher` |

**(options & records)**

`BrokerUri`, `Exchange`, `ExchangeType`, `Mandatory`, `MaxPayloadBytes`, `PublishTimeout`, `RoutingKeyPrefix`

<!-- pubsoft:api-surface:end -->
