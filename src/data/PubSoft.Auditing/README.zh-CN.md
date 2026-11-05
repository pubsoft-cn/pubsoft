---
purpose: PubSoft.Auditing 包级接入指南
audience: 集成方宿主开发者
owner: PubSoft 维护组
last-reviewed: 2026-10-13
---

# PubSoft.Auditing

[English](README.md) | **简体中文**

框架无关的审计原语——无 ASP.NET Core 与 EF Core 依赖。两条写入路径汇聚到同一 sink 管线：

## 请求 scope 审计

`AuditScope` 跟踪一次操作的审计上下文：`SetBusiness` 设业务语义（category/action/target/detail）、`MarkFailed` 标记失败、有界实体变更 collector（`AuditCollector`——默认只记变更字段名，值采集需显式 opt-in 且敏感字段脱敏）。`Finalize` 产出不可变 `AuditRequestSnapshot` 交给宿主的 `IAuditSnapshotSink`。

## 手动业务审计

`IAuditLogWriter.WriteAsync(AuditLogEntry)` 在请求 scope 之外写业务语义审计项——登录、改密、管理员操作：

```csharp
await writer.WriteAsync(new AuditLogEntry(
    new AuditBusinessContext("Auth", "PasswordReset", "User", "42", detail: "via admin"),
    new AuditActorContext("7", "admin", IsAdministrator: true),
    Success: true,
    Metrics: new Dictionary<string, string> { ["retries"] = "0" }));
```

`Metrics` 按键序并入 `Detail`（16 KiB 上限）。手动与请求 scope 审计共用同一 sink、表、保留期与归档行为。

## Actor 归因

`IAuditActorAccessor` 为 opt-in——宿主把 claims 映射为 actor 身份，默认不做任何推断。

## 边界

collector 容量满时整批拒绝而非静默截断。持久化（`EfCoreAuditSnapshotSink`、`AuditRetentionCleanup`、`ArchiveStartedBeforeAsync`）在 `PubSoft.EntityFrameworkCore`；Outbox 转发在 `PubSoft.Auditing.Outbox`；HTTP 捕获中间件在 `PubSoft.AspNetCore`（`UsePubSoftAudit`）。
