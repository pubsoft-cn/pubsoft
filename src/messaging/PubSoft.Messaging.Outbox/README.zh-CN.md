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

// 硬前置：outbox 行使用客户端赋值的 Snowflake `long` id——
// 每副本注册一个共享生成器（workerId 必须唯一 0-31）。
services.AddPubSoftIdGenerator(workerId);

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
    targetModule: "billing",
    aggregateKey: order.Id.ToString(),
    eventType: "orders.order-placed.v1",
    cancellationToken: cancellationToken);
await dbContext.SaveChangesAsync(cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

publisher 不保存不提交。业务数据已在另一事务/context 提交时，本 API 不会追溯使两次写原子。

## 事件契约键与 Payload 序列化

持久事件必须显式传 `eventType`。它会写入 outbox 行，并被内置 MQTT/AMQP dispatcher 用于 topic/routing-key 派生——应把它视为跨服务消息契约的一部分，而不是 CLR 实现细节：

```csharp
await outboxPublisher.PublishAsync(
    new OrderPlaced(order.Id),
    eventType: "orders.order-placed.v1",
    cancellationToken: cancellationToken);
```

显式键有边界：最长 256 字符；不允许空白/控制字符、传输通配符（`*`、`#`、`+`）或前导 `$`。CLR type-name 推断被刻意禁止：它会把路由耦合到重构/程序集名，嵌套 CLR 类型名也可能对传输不安全。仍按旧 CLR-name 行匹配的消费者必须先迁移，或先排空/版本化既有消息后再部署该版本。

payload 默认用 System.Text.Json。需要显式 JSON 策略、或 trimmed/AOT 部署需要源生成 metadata 的宿主，可注册 `IOutboxEventSerializer`；scoped publisher 存在该注册时会解析使用：

```csharp
services.AddSingleton<IOutboxEventSerializer>(
    new OutboxEventJsonSerializer(new JsonSerializerOptions
    {
        TypeInfoResolver = AppJsonContext.Default,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    }));
```

serializer 必须覆盖所有经 outbox 发布的事件类型。实现刻意按运行时事件类型序列化；非法/空 payload 与超限序列化结果仍 fail-closed。

## 租户 Scope

worker 只处理当前 scoped `ITenantContext` 可见的行。无 `IOutboxScopeProvider` 时每轮轮询用一个普通 DI scope——即单租户上下文。多租户宿主既可注册自有 provider，也可显式启用 catalog-backed helper：

```csharp
services.AddScoped<ScopedTenantContext>();
services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<ScopedTenantContext>());
services.AddPubSoftTenantOutboxScopeProvider(options =>
{
    options.IncludeHostScope = true;
    options.MaxTenantsPerPoll = 256;
});
```

`TenantOutboxScopeProvider` 在短生命周期 catalog scope 中解析宿主的 `ITenantAppService`，只枚举启用租户，按确定性顺序创建每租户隔离 async scope，并在 yield 前完成初始化；scope 由 worker 释放。`MaxTenantsPerPoll` 有界——超限会让本轮失败，而不是静默跳过租户。`ScopedTenantOutboxScopeInitializer` 要求处理 scope 内 `ITenantContext` 可解析为 `ScopedTenantContext`。这里刻意不使用 AsyncLocal 的 `AmbientTenantContext`：producer 侧的 ambient 变更不会可靠流入消费 async-enumerator yield 的 worker 控制流。

helper 不负责租户 catalog 授权，也不定义租户生命周期规则。`ITenantAppService.GetListAsync` 仍是宿主实现且未分页的契约，catalog 实现必须保证启用租户规模在运营边界内。自定义租户上下文可替换 `IOutboxTenantScopeInitializer`；自定义发现/授权规则可整体替换 `IOutboxScopeProvider`。

## Inbox 与投递语义

`EfCoreInboxMessageProcessor<TDbContext>` 以 `(tenant, consumerName, messageId)` 为 receipt 键。调用时要求干净 DbContext。receipt 插入、handler 的数据库变更与最终保存共享一个事务；handler 失败整体回滚，同一消息可重试。跨部署用稳定 consumer 名。`InboxOutboxDispatcher` 自动把 consumer 名绑定到 Outbox 消息 ID。

各 dispatcher 传输实现为每条外发消息附上 `OutboxHeaders`（`x-outbox-id`、`x-outbox-aggkey`、`x-outbox-correlation-id`）——`PubSoft.Messaging.Mqtt`、`PubSoft.Messaging.Amqp` 及自定义 `IOutboxDispatcher` 共享的传输无关去重/关联契约。

Outbox worker 以 lease 领取、分发、再标记已投递。进程可能在外部副作用成功后、记录投递前失败——消息会被再次投递。Inbox 保护其事务内的数据库写入；不能包含邮件、HTTP 调用或其他系统的提交——这些副作用须有自己的幂等键或持久协调策略。

重试用有界指数退避 + 尝试上限；耗尽进 dead letter。`AttemptCount` 在租约认领时递增、不在派发失败时——崩溃损失的投递尝试照样计数。这是刻意的毒消息防护：在派发中途让 worker 崩溃的消息必须最终耗尽，而不是无限循环；dead letter（而非静默丢失）就是供排查与重放的设计终点。lease token 阻止迟到 worker，但不能让慢外部操作可逆。监控 dead-letter 与重试计数并提供运维排查/重放流程。receipt 不会按任何计划删除：

```csharp
// 全租户：删除超过保留窗口的 receipt
var deleted = await new InboxRetentionCleanup<AppDbContext>(dbContext, timeProvider)
    .DeleteProcessedBeforeAsync(TimeSpan.FromDays(30), batchSize: 500);

// 或显式限定单租户（"host" 选择 host-scope）
await cleanup.DeleteProcessedBeforeAsync(retention, tenantKey: tenant.TenantKey);
```

启用清理前按最大重放/重投递窗口与隐私义务定保留期；调用节奏（cron、宿主 job）归宿主。审计侧对应原语是 `PubSoft.EntityFrameworkCore` 的 `AuditRetentionCleanup`（另支持 export-before-delete 归档）。

## Worker 状态与健康接线

`OutboxWorkerStatusTracker` 以单例注册，记录轮询生命周期与批次结果：最近轮询开始/完成时间、连续迭代失败次数（含异常类型名）、最近非空批次的认领/投递计数、累计投递/派发失败总数。空轮询不覆盖批次计数器，快照始终描述最近一次非空认领。快照刻意保持低基数——只有异常类型名、不含消息文本；计数跨租户聚合——`Snapshot()` 输出可安全暴露到 health 端点或 metrics 而不泄漏 payload 或租户键。宿主在其上自建健康检查：`ConsecutivePollFailures` 非零、或 `LastPollCompletedUtc` 超过宿主选定的轮询间隔倍数时报告 degraded。投递计数只是运维信号、不构成 exactly-once 证据——派发与标记投递之间崩溃仍会重投。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Messaging.Outbox`: `EfCoreInboxMessageProcessor<TDbContext>` `IInboxMessageProcessor` `IOutboxDispatcher` `IOutboxEventSerializer` `IOutboxMessageStore` `IOutboxPublisher` `IOutboxScopeProvider` `InboxOutboxDispatcher` `InboxReceipt` `InboxRetentionCleanup<TDbContext>` `OutboxDeadLetterPolicy` `OutboxEventJsonSerializer` `OutboxMessage` `OutboxMessageStatus` `OutboxModelBuilderExtensions` `OutboxOptions` `OutboxProcessor` `OutboxPublisher` `OutboxServiceCollectionExtensions` `PubSoftMessagingOutboxMarker`

## 验证

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~OutboxTests
bash scripts/verify-package-consumer.sh
```

集成测试覆盖 SQLite 行为与 opt-in MySQL 8.4 场景。不为其他数据库 provider 确立行为，也不让外部副作用变 exactly-once。
