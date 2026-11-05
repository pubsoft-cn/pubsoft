---
purpose: PubSoft.Messaging.Outbox 包级接入指南
audience: 集成方宿主开发者
owner: PubSoft 维护组
last-reviewed: 2026-10-13
---

# PubSoft.Messaging.Outbox

[English](README.md) | **简体中文**

`PubSoft.Messaging.Outbox` 在业务 DbContext 中 staging 持久消息，并提供轮询 worker 与 Inbox receipt 处理器。投递为 at-least-once。不枚举租户、不注册应用 handler、不按计划删 receipt、不保证外部副作用 exactly-once。（`InboxRetentionCleanup` 提供显式有界删除原语——见下。）

## 模型与注册

宿主的 `PubSoftDbContext` 须配置 Outbox 模型并把业务实体放在同一 context：

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

注册 scoped publisher/store 与 processor，再显式加 hosted worker 及其 dispatcher：

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

worker 为 opt-in。`IOutboxDispatcher` 注册归宿主；不要为同一 worker 注册多个 dispatcher，除非宿主提供自己的路由 dispatcher。

## 发布事务

`PublishAsync` 只把 Outbox 行加入当前 DbContext——与业务变更一起 `SaveChanges` 或显式事务提交：

```csharp
await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
dbContext.Orders.Add(order);
await outboxPublisher.PublishAsync(new OrderPlaced(order.Id),
    targetModule: "billing", aggregateKey: order.Id.ToString(), cancellationToken: cancellationToken);
await dbContext.SaveChangesAsync(cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

publisher 不保存不提交。业务数据已在另一事务/context 提交时，本 API 不会追溯使两次写原子。

## 租户 Scope

worker 只处理当前 scoped `ITenantContext` 可见的行。无 `IOutboxScopeProvider` 时每轮轮询用一个普通 DI scope——即单租户上下文。多租户宿主须注册 `IOutboxScopeProvider`，为 host 与每个待处理租户产出正确初始化的 async scope。本包不发现租户、不决定 worker 有权枚举哪些租户。

示例 catalog 与 initializer 是宿主契约而非 PubSoft API；每个 yield 的 scope 由 worker 释放，`ITenantContext` 须在解析 `OutboxProcessor` 前初始化。

## Inbox 与投递语义

`EfCoreInboxMessageProcessor<TDbContext>` 以 `(tenant, consumerName, messageId)` 为 receipt 键。调用时要求干净 DbContext。receipt 插入、handler 的数据库变更与最终保存共享一个事务；handler 失败整体回滚，同一消息可重试。跨部署用稳定 consumer 名。`InboxOutboxDispatcher` 自动把 consumer 名绑定到 Outbox 消息 ID。

Outbox worker 以 lease 领取、分发、再标记已投递。进程可能在外部副作用成功后、记录投递前失败——消息会被再次投递。Inbox 保护其事务内的数据库写入；不能包含邮件、HTTP 调用或其他系统的提交——这些副作用须有自己的幂等键或持久协调策略。

重试用有界指数退避 + 尝试上限；耗尽进 dead letter。lease token 阻止迟到 worker，但不能让慢外部操作可逆。监控 dead-letter 与重试计数并提供运维排查/重放流程。receipt 不会按任何计划删除：

```csharp
// 全租户：删除超过保留窗口的 receipt
var deleted = await new InboxRetentionCleanup<AppDbContext>(dbContext, timeProvider)
    .DeleteProcessedBeforeAsync(TimeSpan.FromDays(30), batchSize: 500);

// 或显式限定单租户（"host" 选择 host-scope）
await cleanup.DeleteProcessedBeforeAsync(retention, tenantKey: tenant.TenantKey);
```

启用清理前按最大重放/重投递窗口与隐私义务定保留期；调用节奏（cron、宿主 job）归宿主。审计侧对应原语是 `PubSoft.EntityFrameworkCore` 的 `AuditRetentionCleanup`（另支持 export-before-delete 归档）。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Messaging.Outbox`: `EfCoreInboxMessageProcessor<TDbContext>` `IInboxMessageProcessor` `IOutboxDispatcher` `IOutboxMessageStore` `IOutboxPublisher` `IOutboxScopeProvider` `InboxOutboxDispatcher` `InboxReceipt` `InboxRetentionCleanup<TDbContext>` `OutboxDeadLetterPolicy` `OutboxMessage` `OutboxMessageStatus` `OutboxModelBuilderExtensions` `OutboxOptions` `OutboxProcessor` `OutboxPublisher` `OutboxServiceCollectionExtensions` `PubSoftMessagingOutboxMarker`

## 验证

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~OutboxTests
bash scripts/verify-package-consumer.sh
```

集成测试覆盖 SQLite 行为与 opt-in MySQL 8.4 场景。不为其他数据库 provider 确立行为，也不让外部副作用变 exactly-once。
