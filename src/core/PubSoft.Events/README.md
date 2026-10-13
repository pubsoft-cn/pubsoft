# PubSoft.Events

**English** | [简体中文](README.zh-CN.md)


`PubSoft.Events` provides in-process domain event dispatch. It supports awaited synchronous dispatch and a bounded background queue, but it does not persist events, coordinate across processes, or recover queued events after a restart. Use `PubSoft.Messaging.Outbox` when events must commit durably with database changes.

## Registration

Register the package and handler implementations with the lifetimes they need. The dispatcher is a singleton; it creates a DI scope for each dispatch so handlers can be scoped:

```csharp
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Events;

services.AddPubSoftEvents(options => options.Capacity = 2_000);
services.AddScoped<IDomainEventHandler<OrderSubmitted>, UpdateOrderProjection>();
```

`Capacity` must be between 1 and 100,000; the default is 1,000. `AddPubSoftEvents` also registers a hosted background service, so `PublishAsync` begins consuming once the host starts.

Prefer immutable event payloads. The queue passes the event object reference within the process; it does not serialize or snapshot the payload.

## Dispatch Modes

Use `DispatchAsync` when the caller needs handlers to finish before continuing:

```csharp
await dispatcher.DispatchAsync(new OrderSubmitted(orderId), cancellationToken);
```

Manual handlers registered with `RegisterHandler<TEvent>` run first, followed by DI-registered handlers of the exact event type in registration order. A handler exception propagates and stops the remaining synchronous dispatch. Caller cancellation propagates.

Use `PublishAsync` to enqueue work for the background service:

```csharp
await dispatcher.PublishAsync(new OrderSubmitted(orderId), cancellationToken);
```

The queue has one reader and processes events sequentially. When it reaches capacity, publishing waits for space and observes cancellation; it does not drop events silently. `PublishAsync` completing only means the event was accepted into the in-memory queue, not that handlers completed. Handler exceptions are logged and isolated so subsequent handlers/events can continue; cancellation during host shutdown stops the reader, and queued events are not guaranteed to finish.

## Delivery Boundary

This dispatcher is best-effort and local to one service process. It does not provide durable delivery, retry, distributed ordering, or cross-instance deduplication. Do not use it as the only record of work that must survive process failure. For database-backed reliable delivery, stage an Outbox message in the same DbContext transaction as the business update and dispatch it through the Outbox worker.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Events`: `DomainEvent` `DomainEventBackgroundService` `DomainEventDispatcher` `DomainEventOptions` `DomainEventServiceCollectionExtensions` `IDomainEvent` `IDomainEventDispatcher` `IDomainEventHandler<TEvent>`

## Verification

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~DomainEventsTests
bash scripts/verify-package-consumer.sh
```