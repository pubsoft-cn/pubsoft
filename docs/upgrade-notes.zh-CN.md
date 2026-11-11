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

## post-alpha.16 加固（外部审计波次）

- **作用域删除收紧（R1）。** 租户/成员作用域下，删除 `TenantId`/`MemberId` 为空的实体现在与其他越权写一样抛错——此前空作用域 ID 绕过删除保护，作用域调用方可触及宿主共享记录。
- **审计查询 fail-closed（R2）。** `AuditLogQueryService.GetListAsync` 不再把缺失的租户上下文当作平台管理员：跨租户读取须显式传 `includeAllTenants: true`（新增可选参数——签名兼容、语义变更）。
- **加密迁移加固（R5/R6）。** `MigrateAsync` 返回 `EncryptionMigrationReport`（原 `Task<int>`）；无法解密的值跳过计数，不再静默当明文覆盖——`treatUndecryptableAsPlaintext` 保留为危险 opt-in；更新按原值 compare-and-swap，并发写入者被跳过。
- **诊断不再炸启动（R7）。** `EncryptionStartupDiagnostics` 把所有探针失败转为 Error 条目；`StartupDiagnosticsHostedService` 隔离提供者异常为逐条 Error 项。
- `StartupDiagnosticsCollector.ToJson()` 改用 System.Text.Json（R3）——detail 中的控制字符不再破坏 JSON 解析。

## alpha.15 -> alpha.16

- **破坏性（静默）：`AuditMiddlewareOptions` 采集开关默认关。** alpha.15 新增的 `CaptureClientIp`/`CaptureUserAgent`/`CaptureActor` 为隐私优先 opt-in，默认 `false`。`new AuditMiddlewareOptions()` 因此写出的审计行缺 ClientIp/UserAgent/Subject——升级消费方静默丢失取证字段。`UsePubSoftAudit` 现于三开关全关时在启动输出警告；显式开启可恢复采集。
- `UsePubSoftCorrelationId()`/`UsePubSoftAudit()` 向 `UseMiddleware` 传可空 options 对象致宿主启动崩溃（alpha.15）。alpha.16 已按 SecurityHeaders null-guard 范式修复；PUBSOFT055 收录该模式。
- `AesGcmEncryptor.Decrypt` 现抛 `PubSoftDecryptionException`（`CryptographicException` 子类，携带 `Reason`/`KeyId`）。`catch (CryptographicException)` 兼容；`Assert.Throws<CryptographicException>` 等精确类型断言不兼容。

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
