# PubSoft.EntityFrameworkCore

**English** | [简体中文](README.zh-CN.md)


`PubSoft.EntityFrameworkCore` adds an optional EF Core 10 adapter over the scope and soft-delete contracts in `PubSoft.Domain`. It has no ABP dependency. The current slice contains `PubSoftDbContext`, scope-safe repositories, scoped unit-of-work registration, `IgnoreScopeFilters()`, bounded manual query diagnostics, and the optional `PubSoft.Auditing` pre-save capture hook. Audit persistence and provider-specific integrations remain separate work.
`PubSoft.EntityFrameworkCore` adds an optional EF Core 10 adapter over the scope and soft-delete contracts in `PubSoft.Domain`. It has no ABP dependency. The current slice contains `PubSoftDbContext`, scope-safe repositories, scoped unit-of-work registration, `IgnoreScopeFilters()`, and bounded manual query diagnostics; auditing and provider-specific integrations remain separate work.

## Scope Filters

`PubSoftDbContext` registers named filters for `IMultiTenant`, `IMemberScoped`, and `ISoftDelete` entities. Filter expressions read context instance properties so a cached EF model does not capture the scope from the first context that created it.

- Tenant filtering matches `TenantId` to `ITenantContext.TenantId`; a null current tenant sees only rows whose tenant ID is null.
- Member filtering applies only when `IMemberScopeContext.IsMemberScoped` is true and the current member ID is non-null. Otherwise it does not restrict rows by member.
- `IgnoreScopeFilters()` disables only tenant and member filtering. The soft-delete filter remains active.
- `IgnoreQueryFilters()` disables every named filter and can expose deleted rows; reserve it for explicit audit or recovery paths.

For cross-tenant lookups that should still hide deleted rows, use `query.IgnoreScopeFilters()`; do not replace it with EF's parameterless `IgnoreQueryFilters()`.

The host supplies the current tenant/member context implementations when constructing its `DbContext`. A typical derived context passes those contracts to the base constructor and exposes its `DbSet` properties:

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

Register the repository and unit of work as scoped services so they share the host's scoped `DbContext`:

```csharp
services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
services.AddPubSoftRepository<Order, long, AppDbContext>();
services.AddPubSoftUnitOfWork<AppDbContext>();
```

`IRepository<TEntity, TKey>` contracts live in `PubSoft.Domain`; `IEfCoreRepository` additionally exposes `DbSet<TEntity>`. `FindAsync` uses a filtered primary-key query and checks tracked instances against the current scope, so a row previously loaded through an explicit filter bypass is not returned by an ordinary lookup. Key lookup currently supports single-column primary keys only. Repository update/delete operations reject entity instances outside the active non-null tenant/member scope.

## Save Behavior

For added `IMultiTenant` entities, a missing `TenantId` is assigned from the current tenant context. An explicitly set tenant ID is preserved. Removing an `ISoftDelete` entity changes it to `IsDeleted = true` and records `DeletionTime` in UTC; non-soft-delete entities retain EF's normal delete behavior. Both synchronous and asynchronous `SaveChanges` paths apply these rules.

An optional `ISaveChangesHook` runs after tenant assignment and before soft-delete conversion on both save paths. It can inspect or stage the original tracked change intent; if it throws, `SaveChanges` is not called. The hook is synchronous and must not perform I/O. `AddPubSoftSaveChangesHook<TDbContext, THook>()` registers it scoped.

`AddPubSoftAuditChangeCapture()` registers a scoped, bounded `IAuditChangeCollector`, `IAuditScope`, and `AuditChangeCaptureHook`. The derived `PubSoftDbContext` must accept and pass its injected `ISaveChangesHook` to the base constructor. Capture records entity type, key, change kind, timestamp, and changed property names. Values are off by default; when enabled, configured sensitive property names are replaced with `[REDACTED]`. Exceeding the collector capacity rejects the entire pending capture batch and prevents the database save. `IAuditScope` combines caller-supplied request metadata, business semantics, and the collector snapshot into an immutable in-memory result; it does not capture request bodies or exception messages. The optional `PubSoft.AspNetCore` `UsePubSoftAudit()` middleware finalizes the scope and sends snapshots to an `IAuditSnapshotSink`.

For durable EF storage, expose `DbSet<AuditLogRecord>` and `DbSet<AuditEntityChangeRecord>` on the derived context, call `modelBuilder.ConfigurePubSoftAuditPersistence()` after `base.OnModelCreating(modelBuilder)`, and register `AddPubSoftEfCoreAuditPersistence<AppDbContext>()`. The scoped sink writes the log and its change records through the host context. Both record types implement `IMultiTenant`; `PubSoftDbContext` assigns the active tenant and applies its dynamic tenant filter. The EF model must discover both DbSets before the base scope filters are built. Persisted value JSON only contains values already admitted by the collector's opt-in/redaction policy. Outbox forwarding is not included.

Authenticated actor attribution is opt-in through `AuditMiddlewareOptions.CaptureActor` and a host-implemented `IAuditActorAccessor`; PubSoft does not infer claim names or administrator roles. Review that mapping and the identity retention policy per host. Do not place identity or administrator data in `Detail` as a workaround.

The SQLite relational tests exercise context changes against one cached EF model, automatic tenant assignment, selective scope bypass, scope-safe repository lookup/write behavior, scoped registrations, unit-of-work saving, and soft-delete interception. This package does not provide a distributed tenant/member context, database provider, or cross-tenant authorization policy.

## Query Diagnostics

`DbQueryDiagnostics` is an explicit execution wrapper, not an automatically installed EF command interceptor. Register it with `AddPubSoftDbQueryDiagnostics()` and call `ExecuteAsync` or `ExecuteNonQueryAsync` around the operation to measure. It validates sampling/threshold/capacity options, bounds tracked query templates and latency reservoirs (each capacity is capped at 100,000), and uses atomic counters for concurrent operations. Logs and snapshots contain only a validated operation name and a SHA-256 SQL fingerprint; raw SQL, parameter values, and exception messages are never retained or logged. The SQL byte buffer used to compute the fingerprint is cleared after hashing.

## Query Diagnostics

`DbQueryDiagnostics` is an explicit execution wrapper, not an automatically installed EF command interceptor. Register it with `AddPubSoftDbQueryDiagnostics()` and call `ExecuteAsync` or `ExecuteNonQueryAsync` around the operation to measure. It validates sampling/threshold/capacity options, bounds tracked query templates and latency reservoirs, and uses atomic counters for concurrent operations. Logs and snapshots contain only a validated operation name and a SHA-256 SQL fingerprint; raw SQL, parameter values, and exception messages are never retained or logged. The SQL byte buffer used to compute the fingerprint is cleared after hashing.

## Audit Retention and Archival

`AuditRetentionCleanup<TDbContext>` performs explicit, bounded-batch cleanup of persisted `AuditLogRecord` rows (and their `AuditEntityChangeRecord` children). It is invoked directly by the host — nothing schedules or triggers it automatically.

```csharp
var cleanup = new AuditRetentionCleanup<AppDbContext>(dbContext, timeProvider);

// Delete audit rows older than the window (all tenants)
await cleanup.DeleteStartedBeforeAsync(TimeSpan.FromDays(90), batchSize: 500, ct);

// Or restrict deletion to one tenant (null = host-scope rows only)
await cleanup.DeleteStartedBeforeAsync(TimeSpan.FromDays(90), tenantId: 7);
```

`ArchiveStartedBeforeAsync` adds export-before-delete: each batch is loaded **with** its entity changes, handed to a host `exportAsync` callback, and deleted only after export succeeds — a failed export leaves the batch in place. When the callback writes history rows to the *same* DbContext, archive and delete commit atomically in one `SaveChanges`; for other destinations (file/object store) the semantics are at-least-once. The return value counts deleted audit-log rows only.

## Manual Audit Writes

`AddPubSoftEfCoreAuditPersistence<T>` also registers `IAuditLogWriter` (`PubSoft.Auditing`) for business-semantic audit entries outside an HTTP request scope:

```csharp
await auditWriter.WriteAsync(new AuditLogEntry(
    new AuditBusinessContext("Auth", "PasswordReset", "User", targetId: "42", detail: "via admin"),
    Actor: new AuditActorContext("7", "admin", IsAdministrator: true),
    Success: true,
    Metrics: new Dictionary<string, string> { ["retries"] = "0" }));
```

Manual entries flow through the same `IAuditSnapshotSink` pipeline as middleware-captured entries, so they share the same table, retention, and archival behavior. `Metrics` are rendered deterministically into `Detail` (bounded to 16 KiB).

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.EntityFrameworkCore`: `AuditChangeCaptureHook` `DbQueryDiagnostics` `DbQueryDiagnosticsOptions` `DbQueryDiagnosticsServiceCollectionExtensions` `DiagnosticSnapshot` `EfCorePermissionGrantManager<TDbContext>` `EfCorePermissionGrantProvider<TDbContext>` `EfCoreQueryExtensions` `EfCoreRepository<TEntity, TKey, TDbContext>` `EfCoreRoleDataScopeProvider<TDbContext>` `EfCoreServiceCollectionExtensions` `IEfCoreRepository<TEntity, TKey>` `IPubSoftUnitOfWork<TDbContext>` `ISaveChangesHook` `IdentityModelBuilderExtensions` `PermissionGrantModelBuilderExtensions` `PubSoftDbContext` `PubSoftUnitOfWork<TDbContext>` `QuerySnapshot` `RoleDataScopeModelBuilderExtensions` `TenantModelBuilderExtensions`
- `PubSoft.EntityFrameworkCore.Auditing`: `AuditEntityChangeRecord` `AuditLogRecord` `AuditPersistenceModelBuilderExtensions` `AuditPersistenceServiceCollectionExtensions` `AuditRetentionCleanup<TDbContext>` `EfCoreAuditSnapshotSink<TDbContext>`
- `PubSoft.EntityFrameworkCore.Migrations`: `DatabaseMigrationService` `DatabaseMigrationServiceCollectionExtensions` `EfCoreDatabaseSchemaMigrator<TDbContext>` `IDataSeeder` `IDatabaseMigrationService` `IDatabaseSchemaMigrator`
