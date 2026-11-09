---
purpose: package-level hosting guide for PubSoft.Scheduling.EntityFrameworkCore
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Scheduling.EntityFrameworkCore

**English** | [简体中文](https://github.com/pubsoft-cn/pubsoft/blob/main/src/platform/PubSoft.Scheduling.EntityFrameworkCore/README.zh-CN.md)

Durable `IStateStore` for `PubSoft.Scheduling` on EF Core:

```csharp
services.AddPubSoftEfCoreSchedulerStateStore<AppDbContext>();
```

## Semantics

- Each operation creates its own async DI scope + `DbContext`; state rows are isolated by the ambient `ITenantContext`
- Unique logical job key; custom state supports expiry and explicit removal; last-result rows are retained 7 days
- The default `MemoryStateStore` in `PubSoft.Scheduling` is single-process and volatile — swap it out for persistence

## Boundaries

This store gives durable single-writer semantics. It does **not** provide job leases, cluster-wide mutual exclusion, or misfire recovery — multi-node scheduling needs an external coordinator (e.g. `IDistributedLock`). See [PubSoft.Scheduling](../../platform/PubSoft.Scheduling/README.md).

## API surface

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**Registration**

| Method |
|---|---|
| `AddPubSoftEfCoreSchedulerStateStore` |
| `ConfigurePubSoftSchedulerState` |

**(options & records)**

`ExpiresAt`, `KeyHash`, `TenantId`, `TenantScope`, `UpdatedAt`, `ValueJson`

<!-- pubsoft:api-surface:end -->
