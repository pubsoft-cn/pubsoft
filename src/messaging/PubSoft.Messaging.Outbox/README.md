---
purpose: package-level hosting guide for PubSoft.Messaging.Outbox
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Messaging.Outbox

**English** | [简体中文](README.zh-CN.md)


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
    targetModule: "billing", aggregateKey: order.Id.ToString(), cancellationToken: cancellationToken);
await dbContext.SaveChangesAsync(cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

The publisher does not save or commit. If business data was already committed in another transaction/context, this API does not retroactively make the two writes atomic.

## Tenant Scopes

The worker processes only rows visible to the current scoped `ITenantContext`. With no `IOutboxScopeProvider`, each poll uses one ordinary DI scope and therefore one tenant context. Multi-tenant hosts must register an `IOutboxScopeProvider` that yields correctly initialized async scopes for the host and each tenant they intend to process. The package does not discover tenants or decide which tenants the worker is authorized to enumerate.

```csharp
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Messaging.Outbox;

public sealed class TenantOutboxScopeProvider(
    IServiceScopeFactory scopeFactory,
    ITenantCatalog tenantCatalog) : IOutboxScopeProvider
{
    public async IAsyncEnumerable<AsyncServiceScope> CreateScopesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var tenantId in tenantCatalog.GetEnabledTenantIdsAsync(cancellationToken))
        {
            var scope = scopeFactory.CreateAsyncScope();
            try
            {
                scope.ServiceProvider.GetRequiredService<ITenantScopeInitializer>().SetTenant(tenantId);
            }
            catch
            {
                await scope.DisposeAsync();
                throw;
            }

            yield return scope;
        }
    }
}
```

The example's catalog and initializer are host contracts, not PubSoft APIs. Ensure each yielded scope is disposed by the worker and that its `ITenantContext` is initialized before resolving `OutboxProcessor`.

## Inbox and Delivery Semantics

`EfCoreInboxMessageProcessor<TDbContext>` uses `(tenant, consumerName, messageId)` as its receipt key. It requires a clean DbContext when called. The receipt insert, handler's database changes, and final save share a transaction; handler failure rolls them all back, so the same message can be retried. Use a stable consumer name across deployments. `InboxOutboxDispatcher` binds that consumer name to the Outbox message ID automatically.

The Outbox worker claims with a lease, dispatches, then marks the row delivered. A process can fail after an external side effect succeeds but before delivery is recorded, so the message may be delivered again. Inbox protects database writes in its transaction; it cannot include an email, HTTP call, or another system's commit. Give those effects their own idempotency key or durable coordination strategy.

Retries use bounded exponential delay and an attempt limit; exhausted messages move to dead letter. Lease tokens fence late workers, but do not make a slow external operation reversible. Monitor dead-letter and retry counts and provide an operator process for investigation/replay. Receipts are not deleted on any schedule. `InboxRetentionCleanup<TDbContext>` performs explicit bounded-batch deletion:

```csharp
// All tenants: delete receipts older than the retention window
var deleted = await new InboxRetentionCleanup<AppDbContext>(dbContext, timeProvider)
    .DeleteProcessedBeforeAsync(TimeSpan.FromDays(30), batchSize: 500);

// Or restrict to one tenant's receipts explicitly ("host" selects host-scope)
await cleanup.DeleteProcessedBeforeAsync(retention, tenantKey: tenant.TenantKey);
```

Define the retention window from the maximum replay/redelivery window and privacy obligations before enabling cleanup; invocation cadence (cron, host job) is the host's responsibility. The audit-log counterpart `AuditRetentionCleanup` in `PubSoft.EntityFrameworkCore` additionally supports archive-before-delete via an export callback.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Messaging.Outbox`: `EfCoreInboxMessageProcessor<TDbContext>` `EfCoreOutboxPublisher<TDbContext>` `EfCoreOutboxStore<TDbContext>` `IInboxMessageProcessor` `IOutboxDispatcher` `IOutboxPublisher` `IOutboxScopeProvider` `IOutboxStore` `InboxOutboxDispatcher` `InboxReceipt` `InboxRetentionCleanup<TDbContext>` `OutboxBackgroundService` `OutboxMessage` `OutboxMessageStatus` `OutboxModelBuilderExtensions` `OutboxOptions` `OutboxProcessor` `OutboxServiceCollectionExtensions`

## Verification

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~OutboxTests
bash scripts/verify-package-consumer.sh
```

The integration tests exercise SQLite behavior and opt-in MySQL 8.4 cases. They do not establish behavior for other database providers or make external side effects exactly-once.