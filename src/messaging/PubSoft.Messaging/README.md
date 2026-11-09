---
purpose: package-level hosting guide for PubSoft.Messaging
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Messaging

**English** | [简体中文](https://github.com/pubsoft-cn/pubsoft/blob/main/src/messaging/PubSoft.Messaging/README.zh-CN.md)


`PubSoft.Messaging` defines a transport-neutral publisher contract, a JSON message envelope, and an explicit handler dispatcher. It does not provide a broker, persistence, delivery guarantees, retries, or deduplication. Use `PubSoft.Messaging.Outbox` when a message must be staged transactionally with database changes; a transport adapter must implement `IMessagePublisher`.

## Envelope

Use a stable, versioned message key that is independent of CLR type and assembly names:

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

The envelope stores an opaque message ID, explicit message type key, cloned `JsonElement` payload, occurrence time, and optional source/target labels. IDs are limited to 128 non-control characters; message keys and labels are limited to 256. Payload JSON is not used to infer a runtime type. Publishers and consumers must agree on the key, payload schema, and `JsonSerializerOptions` independently of package transport.

## Handler Registration and Dispatch

Register each key explicitly against a payload type and one or more handlers:

```csharp
var dispatcher = new MessageDispatcher();
dispatcher.Register("orders.order-created.v1", new BillingOrderCreatedHandler());
await dispatcher.DispatchAsync(envelope, cancellationToken);
```

Message keys are ordinal and case-sensitive. Multiple handlers for one key run sequentially in registration order; registering the same key for a different payload type throws immediately. An unknown key fails closed with `InvalidOperationException`; malformed or null payload data fails dispatch rather than being dynamically resolved. Cancellation is checked before handlers run and passed to each handler.

`MessageDispatcher` keeps handler instances for its lifetime. Register thread-safe instances whose dependencies match that lifetime, or build a transport-specific scoped dispatch layer that creates the dispatcher/handlers in the appropriate scope. This package does not provide dependency-injection registration or handler discovery.

## Publisher Contract

`IMessagePublisher.PublishAsync(topic, envelope, cancellationToken)` is the only transport-facing contract. Its implementation defines topic validation, persistence, retries, ordering, and delivery behavior; callers should not assume that a successful return means a consumer has processed the message unless that transport explicitly guarantees it. Use opaque IDs and application-level idempotency where duplicate delivery is possible.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Messaging`: `IMessageHandler<TMessage>` `IMessagePublisher` `MessageDispatcher` `MessageEnvelope`

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

<!-- pubsoft:api-surface:end -->

## Verification

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~MessagingContractTests
bash scripts/verify-package-consumer.sh
```