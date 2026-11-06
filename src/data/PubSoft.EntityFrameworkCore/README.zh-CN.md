---
purpose: PubSoft.EntityFrameworkCore 包级接入指南
audience: 集成方宿主开发者
owner: PubSoft 维护组
last-reviewed: 2026-10-13
---

# PubSoft.EntityFrameworkCore

[English](README.md) | **简体中文**

`PubSoft.EntityFrameworkCore` 是基于 `PubSoft.Domain` scope/软删契约的可选 EF Core 10 适配器。无 ABP 依赖。包含 `PubSoftDbContext`、scope 安全仓储、scoped 工作单元注册、`IgnoreScopeFilters()`、有界查询诊断与可选的 `PubSoft.Auditing` pre-save 捕获钩子。

## Scope 过滤器

`PubSoftDbContext` 为 `IMultiTenant`/`IMemberScoped`/`ISoftDelete` 实体注册命名过滤器。过滤表达式读取 context 实例属性——缓存的 EF model 不会固化首个创建它的 context 的 scope。

- 租户过滤将 `TenantId` 匹配 `ITenantContext.TenantId`；当前租户为 null 时只看到 TenantId 为 null 的行
- 成员过滤为 fail-closed：`IMemberScopeContext.IsMemberScoped` 为 true 但当前 member ID 为空时，查询匹配空结果集而非静默放宽；`IsMemberScoped` 为 false 时才不按成员限制
- `IgnoreScopeFilters()` 只关闭租户+成员过滤，软删过滤保持
- `IgnoreQueryFilters()` 关闭全部命名过滤器——会暴露已删行，仅限明确的审计/恢复路径

跨租户查找且仍要隐藏已删行时用 `query.IgnoreScopeFilters()`——不要换成 EF 无参 `IgnoreQueryFilters()`。

宿主构造其 `DbContext` 时提供当前租户/成员 context 实现。典型派生 context 把契约传给基类构造并暴露 `DbSet`：

```csharp
public sealed class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ITenantContext tenant,
    IMemberScopeContext memberScope)
    : PubSoftDbContext(options, tenant, memberScope)
{
    public DbSet<Order> Orders => Set<Order>();
}
```

仓储与工作单元注册为 scoped，与宿主 scoped `DbContext` 共享：

```csharp
services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
services.AddPubSoftRepository<Order, long, AppDbContext>();
services.AddPubSoftUnitOfWork<AppDbContext>();
```

`IRepository<TEntity, TKey>` 契约在 `PubSoft.Domain`；`IEfCoreRepository` 额外暴露 `DbSet<TEntity>`。`FindAsync` 用带过滤的主键查询并校验 tracked 实例的当前 scope——此前经显式绕过过滤器加载的行不会被普通查找返回。键查找目前仅支持单列主键。仓储 update/delete 拒绝活动 scope（非空租户/成员）之外的实体实例。

## 保存行为

新增 `IMultiTenant` 实体缺 `TenantId` 时自动赋值当前租户；显式设置的租户 ID 保留。删除 `ISoftDelete` 实体转为 `IsDeleted = true` + UTC `DeletionTime`；非软删实体保留 EF 正常删除。同步/异步 `SaveChanges` 路径一致。

可选 `ISaveChangesHook` 在租户赋值之后、软删转换之前的两条保存路径上执行；可检查或暂存原始变更意图，抛异常则不调 `SaveChanges`。钩子为同步且不得做 I/O。`AddPubSoftSaveChangesHook<TDbContext, THook>()` 注册 scoped。

`AddPubSoftAuditChangeCapture()` 注册 scoped 有界 `IAuditChangeCollector`、`IAuditScope` 与 `AuditChangeCaptureHook`；派生 `PubSoftDbContext` 须把注入的 `ISaveChangesHook` 传给基类构造。捕获记录实体类型、键、变更种类、时间戳与变更字段名。值默认不采集；启用后配置的敏感字段名替换为 `[REDACTED]`。collector 容量满时整批拒绝并阻止数据库保存。`IAuditScope` 将调用方提供的请求元数据、业务语义与 collector 快照合并为不可变内存结果——不捕获请求体或异常消息。可选 `PubSoft.AspNetCore` `UsePubSoftAudit()` 中间件 finalize scope 并把快照发给 `IAuditSnapshotSink`。

持久化审计存储：派生 context 暴露 `DbSet<AuditLogRecord>`/`DbSet<AuditEntityChangeRecord>`，`base.OnModelCreating` 后调 `modelBuilder.ConfigurePubSoftAuditPersistence()`，并注册 `AddPubSoftEfCoreAuditPersistence<AppDbContext>()`。审计记录使用 sink 端赋值的 Snowflake `long` id——注册共享 `IIdGenerator`（`PubSoft.DependencyInjection` 的 `services.AddPubSoftIdGenerator(workerId)`）是硬前置，sink 构造期解析，缺失即 DI 失败。scoped sink 经宿主 context 写日志与变更记录。两种记录都实现 `IMultiTenant`；`PubSoftDbContext` 自动赋当前租户并应用动态租户过滤器。EF model 须在基类 scope 过滤器构建前发现两个 DbSet。持久化的值 JSON 只含 collector opt-in/脱敏策略已许可的值。审计 Outbox 转发由可选 `PubSoft.Auditing.Outbox` 提供。

Actor 归因经 `AuditMiddlewareOptions.CaptureActor` + 宿主实现的 `IAuditActorAccessor` opt-in——PubSoft 不推断 claim 名或管理员角色；按宿主审查该映射与身份保留策略，不要把身份/管理员数据塞进 `Detail`。

## 查询诊断

`DbQueryDiagnostics` 是显式执行包装器，非自动安装的 EF 命令拦截器。注册 `AddPubSoftDbQueryDiagnostics()` 并用 `ExecuteAsync`/`ExecuteNonQueryAsync` 包裹待测操作。校验采样/阈值/容量选项，跟踪的查询模板与延迟样本池各自有界（上限 100,000），计数器原子。日志与快照只含校验过的操作名与 SHA-256 SQL 指纹——原始 SQL、参数值、异常消息永不保留或记录；计算指纹用的 SQL 字节缓冲用后清零。

## 审计保留与归档

`AuditRetentionCleanup<TDbContext>` 对已持久化的 `AuditLogRecord`（及其 `AuditEntityChangeRecord` 子行）做显式分批清理。由宿主直接调用——无自动调度。

```csharp
var cleanup = new AuditRetentionCleanup<AppDbContext>(dbContext, timeProvider);

// 删除超过窗口的审计行（全租户）
await cleanup.DeleteStartedBeforeAsync(TimeSpan.FromDays(90), batchSize: 500, ct);

// 或限定单租户（null = 仅 Host 域行）
await cleanup.DeleteStartedBeforeAsync(TimeSpan.FromDays(90), tenantId: 7);
```

`ArchiveStartedBeforeAsync` 增加 export-before-delete：每批**连同 EntityChanges** 加载，交给宿主 `exportAsync` 回调，导出成功才删除——导出失败该批保留。回调往**同一** DbContext 写历史行时归档+删除在一次 `SaveChanges` 原子提交；其他目的地（文件/对象存储）为 at-least-once。返回值只计删除的 audit-log 行数。

## 手动审计写入

`AddPubSoftEfCoreAuditPersistence<T>` 同时注册 `IAuditLogWriter`（`PubSoft.Auditing`）用于 HTTP 请求 scope 外的业务语义审计：

```csharp
await auditWriter.WriteAsync(new AuditLogEntry(
    new AuditBusinessContext("Auth", "PasswordReset", "User", targetId: "42", detail: "via admin"),
    Actor: new AuditActorContext("7", "admin", IsAdministrator: true),
    Success: true,
    Metrics: new Dictionary<string, string> { ["retries"] = "0" }));
```

手动项与中间件捕获项走同一 `IAuditSnapshotSink` 管线——同表、同保留期、同归档。`Metrics` 按键序并入 `Detail`（16 KiB 上限）。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.EntityFrameworkCore`: `AuditChangeCaptureHook` `DbQueryDiagnostics` `DbQueryDiagnosticsOptions` `DbQueryDiagnosticsServiceCollectionExtensions` `EncryptionModelBuilderExtensions` `DiagnosticSnapshot` `EfCorePermissionGrantManager<TDbContext>` `EfCorePermissionGrantProvider<TDbContext>` `EfCoreQueryExtensions` `EfCoreRepository<TEntity, TKey, TDbContext>` `EfCoreRoleDataScopeProvider<TDbContext>` `EfCoreServiceCollectionExtensions` `IEfCoreRepository<TEntity, TKey>` `IPubSoftUnitOfWork<TDbContext>` `ISaveChangesHook` `IdentityModelBuilderExtensions` `PermissionGrantModelBuilderExtensions` `PubSoftDbContext` `PubSoftUnitOfWork<TDbContext>` `QuerySnapshot` `RoleDataScopeModelBuilderExtensions` `TenantModelBuilderExtensions`
- `PubSoft.EntityFrameworkCore.Auditing`: `AuditEntityChangeRecord` `AuditLogRecord` `AuditPersistenceModelBuilderExtensions` `AuditPersistenceServiceCollectionExtensions` `AuditRetentionCleanup<TDbContext>` `EfCoreAuditSnapshotSink<TDbContext>`
- `PubSoft.EntityFrameworkCore.Migrations`: `DatabaseMigrationService` `DatabaseMigrationServiceCollectionExtensions` `EfCoreDatabaseSchemaMigrator<TDbContext>` `IDataSeeder` `IDatabaseMigrationService` `IDatabaseSchemaMigrator`

## 数据库健康检查与 Snowflake 约定

`AddPubSoftEfCoreHealthCheck<TDbContext>(name = "db", probeSql = "SELECT 1")` 注册 `IAppHealthCheck`：开 scope、验 `CanConnectAsync`、执行探测语句——替代每个宿主复制的 `MySqlHealthCheck` 样板。

`builder.ApplySnowflakeIdConventions()`（`OnModelCreating` 中调用）把所有 `long` 主键标为 `ValueGenerated.Never`——替代配合 `SnowflakeIdGenerator` 的逐实体循环（ID 由客户端生成，存储不生成）。
