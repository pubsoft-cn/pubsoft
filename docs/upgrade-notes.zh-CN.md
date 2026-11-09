---
purpose: 下游消费方逐版本升级卡点说明。
audience: 跨版本升级 PubSoft 的消费方。
owner: PubSoft 维护者
last-reviewed: 2026-11-09
---

# 升级说明

## 版本流

| 流 | 渠道 | 面向 |
|---|---|---|
| `0.1.0-alpha.*` | nuget.org + 内部源 | 外部消费、正式发布 |
| `0.1.0-dev.yyyymmdd[.n]` | 仅内部源 | 内部迭代；永不上 nuget.org |

消费 `dev.*` 需要 `packageSources` 里的内部源**以及** `PubSoft.*` 的 `packageSourceMapping` 条目。全局 `*` 映射到 nuget.org 会静默滤掉内部源：restore 只报 NU1102"找不到版本"，不提示是被源映射过滤。先查映射再怀疑没发包。

刚推送后的 NU1102 还可能是缓存抖动：`dotnet restore --no-cache` 重试先行，再排查其他。

## alpha.13 -> 当前

- `System.IdentityModel.Tokens.Jwt` 下限抬至 **8.23.0**。CPM 钉低版本的消费方会遇到 NU1109，抬升钉值即可。
- `PubSoft.Edge.Host` 不是真实包 ID（从未发布）。清理残留的 `PackageVersion` 声明；CPM 对未使用的声明不告警。
- CPM 对缺失声明会报 NU1010：每条 `PackageReference` 都需要对应 `PackageVersion`。
- 新增公开面：`AddPubSoftStartupDiagnosticsHostedService`（启动时输出姿态日志）、`MapPubSoftStartupDiagnostics`（JSON 端点，opt-in）。包级姿态提供者落在 `PubSoft.AspNetCore`（进程内 nonce store）、`PubSoft.Caching.Redis`（缺 multiplexer）、`PubSoft.Authentication.Jwt.AspNetCore`（空 audience）。

## 审计持久化迁移（下游既有审计存储退役路径）

PubSoft 已在 `PubSoft.EntityFrameworkCore` 内置全套 EF 审计持久化——不需要单独 `Auditing.EntityFrameworkCore` 包。

| 既有审计存储件 | PubSoft 对应 |
|---|---|
| AuditLog 表 | `AuditLogRecord`——字段超集（Snowflake Id、TenantId、StartedAt、HttpMethod、RequestPath、ClientIp、UserAgent、SubjectId/Name、IsAdministrator、Success、StatusCode、FailureCode、DurationMilliseconds、Category、Action） |
| EntityChange 表 | `AuditEntityChangeRecord` + `AuditChangeCaptureHook`（SaveChanges 前捕获） |
| 写入服务 | `EfCoreAuditSnapshotSink`——快照+变更原子落库 |
| 归档/清理 | `AuditRetentionCleanup`、`ArchiveStartedBeforeAsync` |
| schema/映射 | `ConfigurePubSoftAuditPersistence`——UTC ticks、ValueGenerated.Never |
| 查询 | `AuditLogQueryService` |

接入：

```csharp
services.AddPubSoftEfCoreAuditPersistence<YourDbContext>();
// DbContext.OnModelCreating:
modelBuilder.ConfigurePubSoftAuditPersistence();
```

仍归宿主：DTO/controller/查询端点与逐服务消息发布。
