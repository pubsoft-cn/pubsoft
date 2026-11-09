---
purpose: Per-version upgrade friction notes for downstream consumers.
audience: PubSoft consumers upgrading between versions.
owner: PubSoft maintainers
last-reviewed: 2026-11-09
---

# Upgrade Notes

## Version streams

| Stream | Where | For |
|---|---|---|
| `0.1.0-alpha.*` | nuget.org + internal feed | external consumers, releases |
| `0.1.0-dev.yyyymmdd[.n]` | internal feed only | internal iteration; never on nuget.org |

Consumers on `dev.*` need the internal feed in `packageSources` **and** a `packageSourceMapping` entry for `PubSoft.*`. A global `*` mapping to nuget.org silently filters the internal feed: restore then reports NU1102 "version not found" without saying the package was filtered by source mapping. Check the mapping before assuming the package was not published.

Right after a push, an NU1102 may also be stale-cache jitter: retry with `dotnet restore --no-cache` before debugging anything else.

## alpha.13 -> current

- `System.IdentityModel.Tokens.Jwt` floor raised to **8.23.0**. Consumers pinning lower in CPM hit NU1109 until their pin is raised.
- `PubSoft.Edge.Host` is not a real package id (never shipped). Remove stale `PackageVersion` entries; CPM does not warn on unused declarations.
- CPM does warn on missing declarations (NU1010): every `PackageReference` needs a `PackageVersion` entry.
- New public surface: `AddPubSoftStartupDiagnosticsHostedService` (logs posture at startup), `MapPubSoftStartupDiagnostics` (JSON endpoint, opt-in). Package-level posture providers ship in `PubSoft.AspNetCore` (process-local nonce store), `PubSoft.Caching.Redis` (missing multiplexer), `PubSoft.Authentication.Jwt.AspNetCore` (empty audience).

## Audit persistence migration (audit-store retirement path)

PubSoft already ships the full EF audit-persistence stack inside `PubSoft.EntityFrameworkCore` — a dedicated `Auditing.EntityFrameworkCore` package is not required.

| existing audit-store piece | PubSoft replacement |
|---|---|
| AuditLog table | `AuditLogRecord` — superset fields (Id Snowflake, TenantId, StartedAt, HttpMethod, RequestPath, ClientIp, UserAgent, SubjectId/Name, IsAdministrator, Success, StatusCode, FailureCode, DurationMilliseconds, Category, Action) |
| EntityChange table | `AuditEntityChangeRecord` + `AuditChangeCaptureHook` (captures before SaveChanges) |
| write service | `EfCoreAuditSnapshotSink` — writes snapshot + changes atomically |
| archive/cleanup | `AuditRetentionCleanup`, `ArchiveStartedBeforeAsync` |
| schema/mapping | `ConfigurePubSoftAuditPersistence` — UTC ticks, ValueGenerated.Never |
| query | `AuditLogQueryService` |

Wiring:

```csharp
services.AddPubSoftEfCoreAuditPersistence<YourDbContext>();
// DbContext.OnModelCreating:
modelBuilder.ConfigurePubSoftAuditPersistence();
```

Stays host-side: DTO/controller/query endpoints and any per-service bus publishing.
