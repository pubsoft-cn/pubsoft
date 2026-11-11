---
purpose: Optional EF Core persistence for PubSoft.Security refresh-rotation and revocation state
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-11-11
---

# PubSoft.Security.EntityFrameworkCore

Restart-durable `IRefreshTokenFamilyStore` and `ITokenDenylist` implementations backed by the host's own DbContext — for deployments that cannot lose rotation or revocation state across restarts and do not (yet) run Redis.

## Scope and Guarantees

- The refresh consume path is one atomic conditional `UPDATE` (single statement with `NOT EXISTS` on the revocation table), so concurrent consumes across instances resolve to exactly one winner — the same guarantee `RedisRefreshTokenFamilyStore` gives via Lua.
- Revoking a family writes a marker row; the consume update re-checks it in the same statement, so revocation wins the race.
- The denylist stores token ids (hashes), not raw tokens; `IsDeniedAsync` only matches unexpired entries.
- Fail-closed bounds: token ids/hashes, subjects, and reasons are length-checked before touching the database; duplicate inserts surface as `InvalidOperationException`, not silent overwrite.

## Setup

```csharp
// host DbContext
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ConfigurePubSoftSecurityState();
}

// registration (host DbContext + IIdGenerator must already be registered)
services.AddDbContext<AppDbContext>(/* provider */);
services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(workerId));
services.AddPubSoftEfCoreRefreshTokenFamilyStore<AppDbContext>();
services.AddPubSoftEfCoreTokenDenylist<AppDbContext>();
services.AddPubSoftRefreshTokenFamilyService();
```

## Options

No options types — bounds live on the contracts' own options (`RefreshTokenFamilyOptions`, `RedisTokenDenylistOptions`-style limits are enforced inside the stores). Entity column lengths: `TokenHash`/`FamilyId` 64, `TokenId` 128, `Subject`/`Reason` 256.

## Residual Host Responsibilities

- Owns the schema: call `ConfigurePubSoftSecurityState` in `OnModelCreating` and ship the migration — the package creates nothing automatically.
- Retention: expired rows are never auto-deleted (unlike Redis TTLs); schedule `PruneExpiredAsync` on both stores.
- The atomic consume relies on a single `UPDATE ... WHERE` statement at the provider's default isolation; it does not depend on explicit transactions, but host retry policies should treat it as idempotent-unsafe like any write.
- For multi-instance scale-out, `PubSoft.Security.Redis` remains the lower-latency shared store; EF is the restart-durable option, not the high-throughput one.

## Public API surface

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**Registration**

| Method |
|---|---|
| `AddPubSoftEfCoreRefreshTokenFamilyStore` |
| `AddPubSoftEfCoreTokenDenylist` |
| `ConfigurePubSoftSecurityState` |

**(options & records)**

`Consumed`, `DeniedAt`, `ExpiresAt`, `FamilyExpiresAt`, `FamilyId`, `IssuedAt`, `Reason`, `Subject`, `TokenExpiresAt`, `TokenHash`, `TokenId`

<!-- pubsoft:api-surface:end -->
