---
purpose: package-level hosting guide for PubSoft.Auditing.Outbox
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Auditing.Outbox

**English** | [简体中文](https://github.com/pubsoft-cn/pubsoft/blob/main/src/data/PubSoft.Auditing.Outbox/README.zh-CN.md)

Routes finalized `AuditRequestSnapshot`s through the durable Outbox instead of writing audit tables directly:

```csharp
services.AddPubSoftAuditOutbox<AppDbContext>();
```

## Semantics

- The versioned snapshot payload stages into the same scoped `DbContext` that owns the request's business transaction — audit and business writes commit atomically.
- Delivery to the audit store is at-least-once; the host must run an `IOutboxDispatcher` + idempotent consumer that persists snapshots.
- This sink does not bind to previously committed transactions — it only covers work saved in the current scope.

## Boundaries

The host owns dispatcher registration, payload privacy/retention review, and consumer idempotency. The direct EF audit sink (`PubSoft.EntityFrameworkCore`) remains available independently — choose one per pipeline.

## API surface

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**Registration**

| Method |
|---|---|
| `AddPubSoftAuditOutbox` |

**(options & records)**

`SchemaVersion`, `Snapshot`

<!-- pubsoft:api-surface:end -->
