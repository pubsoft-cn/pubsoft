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

## post-alpha.16 hardening (external audit wave)

- **Scoped delete tightens (R1).** Under a tenant/member scope, deleting an entity whose `TenantId`/`MemberId` is null now throws like any other out-of-scope write - previously null scope ids bypassed the delete guard and scoped callers could hit host-shared rows.
- **Audit query fail-closed (R2).** `AuditLogQueryService.GetListAsync` no longer treats a missing tenant context as platform admin: cross-tenant reads now require the explicit `includeAllTenants: true` parameter (new optional arg - signature-compatible, semantics change).
- **Encryption migrator hardened (R5/R6).** `MigrateAsync` returns `EncryptionMigrationReport` (was `Task<int>`); undecryptable values are skipped and counted, never silently re-encrypted as plaintext - `treatUndecryptableAsPlaintext` remains as a dangerous opt-in; updates compare-and-swap on the original value so concurrent writers are skipped.
- **Diagnostics cannot crash startup (R7).** `EncryptionStartupDiagnostics` converts every probe failure into an Error entry; `StartupDiagnosticsHostedService` isolates provider exceptions into per-provider Error entries.
- `StartupDiagnosticsCollector.ToJson()` now serializes via System.Text.Json (R3) - control characters in provider details stay parseable.

## alpha.15 -> alpha.16

- **Breaking (silent): `AuditMiddlewareOptions` capture flags default off.** `CaptureClientIp`, `CaptureUserAgent`, `CaptureActor` were added in alpha.15 as privacy-first opt-ins and default to `false`. `new AuditMiddlewareOptions()` therefore writes audit rows without ClientIp/UserAgent/Subject - a silent forensic-data loss for upgrading consumers. `UsePubSoftAudit` now logs a startup warning when all three flags are off; set the flags explicitly to restore capture.
- `UsePubSoftCorrelationId()`/`UsePubSoftAudit()` forwarded a possibly-null options object to `UseMiddleware` and crashed host startup (alpha.15). Fixed in alpha.16 with the SecurityHeaders null-guard pattern; PUBSOFT055 flags the pattern.
- `AesGcmEncryptor.Decrypt` now throws `PubSoftDecryptionException` (a `CryptographicException` subclass carrying `Reason`/`KeyId`). `catch (CryptographicException)` still matches; `Assert.Throws<CryptographicException>` in tests and exact-type checks do not.

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
