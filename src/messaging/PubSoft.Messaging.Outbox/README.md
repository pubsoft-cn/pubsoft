---
purpose: package-level hosting guide for PubSoft.Messaging.Outbox
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Messaging.Outbox

**English** | [简体中文](https://github.com/pubsoft-cn/pubsoft/blob/main/src/messaging/PubSoft.Messaging.Outbox/README.zh-CN.md)


`PubSoft.Messaging.Outbox` stages durable messages in the business DbContext and provides a polling worker and Inbox receipt processor. Delivery is at-least-once. It does not enumerate tenants, register application handlers, delete receipts on a schedule, or make external effects exactly-once. (`InboxRetentionCleanup` provides the explicit bounded deletion primitive — see below.)

## Model and Registration

The host's `PubSoftDbContext` must configure the Outbox model and include its business entities in the same context:

```csharp
using Microsoft.EntityFrameworkCore;
using PubSoft.Messaging.Outbox;

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.ConfigurePubSoftOutbox();
    modelBuilder.Entity<Order>().HasKey(order => order.Id);
}
```

Register the scoped publisher/store and processor, then explicitly add the hosted worker and its dispatcher:

```csharp
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Messaging.Outbox;

// Required: outbox rows use Snowflake `long` ids assigned client-side —
// register one shared generator per replica (workerId must be unique 0-31).
services.AddPubSoftIdGenerator(workerId);

services.AddPubSoftOutbox<AppDbContext>(options =>
{
    options.BatchSize = 50;
    options.MaxAttempts = 5;
});
services.AddScoped<IOutboxDispatcher, BillingDispatcher>();
services.AddPubSoftOutboxWorker();
```

The worker is opt-in. Registering `IOutboxDispatcher` is the host's responsibility; do not register multiple dispatchers for the same worker unless the host supplies its own routing dispatcher.

## Publishing Transaction

`PublishAsync` only adds an Outbox row to the current DbContext. Save it with the business changes in the same `SaveChanges` call or explicit database transaction:

```csharp
await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
dbContext.Orders.Add(order);
await outboxPublisher.PublishAsync(new OrderPlaced(order.Id),
    targetModule: "billing",
    aggregateKey: order.Id.ToString(),
    eventType: "orders.order-placed.v1",
    cancellationToken: cancellationToken);
await dbContext.SaveChangesAsync(cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

The publisher does not save or commit. If business data was already committed in another transaction/context, this API does not retroactively make the two writes atomic.

## Event Contract Keys and Payload Serialization

`eventType` is required for durable events. It is the stable, versioned contract key written to the outbox row and reused by the built-in MQTT/AMQP dispatchers for topic/routing-key derivation. Treat it as part of the cross-service message contract, not as a CLR implementation detail:

```csharp
await outboxPublisher.PublishAsync(
    new OrderPlaced(order.Id),
    eventType: "orders.order-placed.v1",
    cancellationToken: cancellationToken);
```

Explicit keys are bounded: maximum 256 characters, no whitespace/control characters, no transport wildcard characters (`*`, `#`, `+`), and no leading `$`. CLR type-name inference is deliberately not supported: it couples routing to refactors/assembly names and can be unsafe for nested CLR type names. Consumers keyed to older CLR-name rows must be migrated or those rows drained/versioned before deploying this version.

Payload serialization uses System.Text.Json by default. Hosts that need explicit JSON policy or source-generated metadata for trimmed/AOT deployments can register `IOutboxEventSerializer`; the scoped publisher resolves it when present:

```csharp
services.AddSingleton<IOutboxEventSerializer>(
    new OutboxEventJsonSerializer(new JsonSerializerOptions
    {
        TypeInfoResolver = AppJsonContext.Default,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    }));
```

The serializer must cover every event type published through the outbox. It intentionally serializes using the runtime event type; invalid/empty payloads and oversized serialized payloads still fail closed.

## Tenant Scopes

The worker processes only rows visible to the current scoped `ITenantContext`. With no `IOutboxScopeProvider`, each poll uses one ordinary DI scope and therefore one tenant context. Multi-tenant hosts can either register their own provider or opt into the catalog-backed helper:

```csharp
services.AddScoped<ScopedTenantContext>();
services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<ScopedTenantContext>());
services.AddPubSoftTenantOutboxScopeProvider(options =>
{
    options.IncludeHostScope = true;
    options.MaxTenantsPerPoll = 256;
});
```

`TenantOutboxScopeProvider` resolves the host's `ITenantAppService` inside a short-lived catalog scope, enumerates enabled tenants only, sorts them deterministically, creates one isolated async scope per tenant, initializes it before yield, and lets the worker dispose that scope. `MaxTenantsPerPoll` is bounded; exceeding it fails the poll instead of silently skipping tenants. `ScopedTenantOutboxScopeInitializer` requires `ITenantContext` to resolve to `ScopedTenantContext` in the processing scope. Async-local `AmbientTenantContext` is deliberately not used here: producer-side ambient changes do not reliably flow to the worker consuming an async-enumerator yield.

The helper does not authorize the tenant catalog or define tenant lifecycle rules. `ITenantAppService.GetListAsync` is still a host-implemented, unpaged contract, so the catalog implementation must keep the enabled fleet within operational bounds. Custom tenant context implementations can replace `IOutboxTenantScopeInitializer`; custom discovery/authorization rules can replace `IOutboxScopeProvider` entirely.

## Inbox and Delivery Semantics

`EfCoreInboxMessageProcessor<TDbContext>` uses `(tenant, consumerName, messageId)` as its receipt key. It requires a clean DbContext when called. The receipt insert, handler's database changes, and final save share a transaction; handler failure rolls them all back, so the same message can be retried. Use a stable consumer name across deployments. `InboxOutboxDispatcher` binds that consumer name to the Outbox message ID automatically.

Dispatcher transports attach `OutboxHeaders` (`x-outbox-id`, `x-outbox-aggkey`, `x-outbox-correlation-id`) to every published message — the transport-agnostic dedup/correlation contract shared by `PubSoft.Messaging.Mqtt`, `PubSoft.Messaging.Amqp`, and any custom `IOutboxDispatcher`.

The Outbox worker claims with a lease, dispatches, then marks the row delivered. A process can fail after an external side effect succeeds but before delivery is recorded, so the message may be delivered again. Inbox protects database writes in its transaction; it cannot include an email, HTTP call, or another system's commit. Give those effects their own idempotency key or durable coordination strategy.

Retries use bounded exponential delay and an attempt limit; exhausted messages move to dead letter. `AttemptCount` increments at lease claim, not at dispatch failure — a delivery attempt lost to a crash still counts. This is deliberate poison-message protection: a message that crashes the worker mid-dispatch must exhaust eventually instead of looping forever, and dead letter (not silent loss) is the designed destination for inspection and replay. Lease tokens fence late workers, but do not make a slow external operation reversible. Monitor dead-letter and retry counts and provide an operator process for investigation/replay. Receipts are not deleted on any schedule. `InboxRetentionCleanup<TDbContext>` performs explicit bounded-batch deletion:

```csharp
// All tenants: delete receipts older than the retention window
var deleted = await new InboxRetentionCleanup<AppDbContext>(dbContext, timeProvider)
    .DeleteProcessedBeforeAsync(TimeSpan.FromDays(30), batchSize: 500);

// Or restrict to one tenant's receipts explicitly ("host" selects host-scope)
await cleanup.DeleteProcessedBeforeAsync(retention, tenantKey: tenant.TenantKey);
```

Define the retention window from the maximum replay/redelivery window and privacy obligations before enabling cleanup; invocation cadence (cron, host job) is the host's responsibility. The audit-log counterpart `AuditRetentionCleanup` in `PubSoft.EntityFrameworkCore` additionally supports archive-before-delete via an export callback.

## Worker Status and Health Wiring

`OutboxWorkerStatusTracker` is registered as a singleton and records poll lifecycle and batch outcomes: last poll start/completion timestamps, consecutive iteration failures with the exception type name, last non-empty batch claimed/delivered counts, and cumulative delivered/dispatch-failure totals. Empty polls do not overwrite the last-batch counters, so the snapshot keeps describing the most recent non-empty claim. The snapshot is deliberately low-cardinality — exception type names only, never message text; counts aggregate across tenant scopes — so `Snapshot()` output is safe to surface in health endpoints or metrics without leaking payloads or tenant keys. Hosts wire their own health check on top: report degraded when `ConsecutivePollFailures` is non-zero or `LastPollCompletedUtc` is older than a host-chosen multiple of the poll interval. Delivery counts are operational signals, not exactly-once evidence — a crash between dispatch and mark-delivered still redelivers.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Messaging.Outbox`: `EfCoreInboxMessageProcessor<TDbContext>` `EfCoreOutboxPublisher<TDbContext>` `EfCoreOutboxStore<TDbContext>` `IInboxMessageProcessor` `IOutboxDispatcher` `IOutboxEventSerializer` `IOutboxPublisher` `IOutboxScopeProvider` `IOutboxStore` `InboxOutboxDispatcher` `InboxReceipt` `InboxRetentionCleanup<TDbContext>` `OutboxBackgroundService` `OutboxEventJsonSerializer` `OutboxMessage` `OutboxMessageStatus` `OutboxModelBuilderExtensions` `OutboxOptions` `OutboxProcessor` `OutboxServiceCollectionExtensions`

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**Registration**

| Method |
|---|---|
| `AddPubSoftOutbox` |
| `AddPubSoftOutboxWorker` |
| `AddPubSoftTenantOutboxScopeProvider` |
| `ConfigurePubSoftOutbox` |

**(options & records)**

`AggregateKey`, `AttemptCount`, `BaseRetryDelay`, `BatchSize`, `ConsecutivePollFailures`, `ConsumerName`, `CorrelationId`, `DeliveredAt`, `EventType`, `IncludeHostScope`, `LastBatchClaimedCount`, `LastBatchDeliveredCount`, `LastErrorCode`, `LastErrorType`, `LastPollCompletedUtc`, `LastPollStartedUtc`, `LeaseDuration`, `LeaseExpiresAt`, `LeaseToken`, `MaxAttempts`, `MaximumPayloadCharacters`, `MaxRetryDelay`, `MaxTenantsPerPoll`, `MessageId`, `NextAttemptAt`, `OccurredAt`, `PayloadJson`, `PollInterval`, `ProcessedAt`, `Status`, `TargetModule`, `TenantId`, `TenantKey`, `TotalDelivered`, `TotalDispatchFailures`, `TotalPolls`

<!-- pubsoft:api-surface:end -->

## Verification

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~OutboxTests
bash scripts/verify-package-consumer.sh
```

The integration tests exercise SQLite behavior and opt-in MySQL 8.4 cases. They do not establish behavior for other database providers or make external side effects exactly-once.

