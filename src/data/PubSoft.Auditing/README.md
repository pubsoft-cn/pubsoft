---
purpose: package-level hosting guide for PubSoft.Auditing
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Auditing

**English** | [简体中文](https://github.com/pubsoft-cn/pubsoft/blob/main/src/data/PubSoft.Auditing/README.zh-CN.md)

Framework-independent audit primitives — no ASP.NET Core or EF Core dependency. Covers two write paths that converge on a single sink pipeline:

## Request-scope auditing

`AuditScope` tracks one operation's audit context: business semantics via `SetBusiness` (category/action/target/detail), failure marking (`MarkFailed`), and a bounded entity-change collector (`AuditCollector` — default captures change field names only, values require explicit opt-in and sensitive fields are masked). `Finalize` produces an immutable `AuditRequestSnapshot` handed to the host's `IAuditSnapshotSink`.

## Manual business auditing

`IAuditLogWriter.WriteAsync(AuditLogEntry)` writes business-semantic entries outside any request scope — logins, password resets, admin actions:

```csharp
await writer.WriteAsync(new AuditLogEntry(
    new AuditBusinessContext("Auth", "PasswordReset", "User", "42", detail: "via admin"),
    new AuditActorContext("7", "admin", IsAdministrator: true),
    Success: true,
    Metrics: new Dictionary<string, string> { ["retries"] = "0" }));
```

`Metrics` render deterministically into `Detail` (16 KiB bound). Manual and request-scope entries share the same sink, table, retention, and archival behavior.

## Actor attribution

`IAuditActorAccessor` is opt-in — the host maps claims to actor identity; nothing is inferred by default. `PubSoft.AspNetCore` ships `AddCurrentSubjectAuditActor()`, which bridges `ICurrentSubjectAccessor` into `AuditActorContext` so hosts that already resolve a subject need no mapping code.

## Sensitive fields

Entity property values are redacted when either `SensitiveProperties` name-matches
or the property is marked `[AuditSensitive]` — the attribute is the declarative
path, the option the bulk/overrides path; either one triggers `[REDACTED]` in
captured old/new values. Redaction needs `CaptureValues = true`.

## Boundaries

The collector rejects the whole batch when full rather than silently truncating. Durable persistence (`EfCoreAuditSnapshotSink`, `AuditRetentionCleanup`, `ArchiveStartedBeforeAsync`) lives in `PubSoft.EntityFrameworkCore`; Outbox forwarding lives in `PubSoft.Auditing.Outbox`; HTTP capture middleware lives in `PubSoft.AspNetCore` (`UsePubSoftAudit`).

## API surface

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**(options & records)**

`Action`, `Actor`, `AuditGetRequests`, `Business`, `CaptureValues`, `Category`, `ChangedAt`, `Changes`, `ClientIp`, `Detail`, `DisplayName`, `Duration`, `Enabled`, `EntityId`, `EntityType`, `FailureCode`, `HttpMethod`, `IgnoredEntityTypes`, `IsAdministrator`, `MaxChangedPropertiesPerEntity`, `MaxChanges`, `MaxStringValueLength`, `Metrics`, `NewValue`, `OldValue`, `Properties`, `Request`, `RequestPath`, `SensitiveProperties`, `StartedAt`, `StatusCode`, `SubjectId`, `Success`, `TargetId`, `TargetName`, `TargetType`, `UserAgent`

<!-- pubsoft:api-surface:end -->
