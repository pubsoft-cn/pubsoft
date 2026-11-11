---
purpose: package-level hosting guide for PubSoft.Security.Redis
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Security.Redis

**English** | [简体中文](https://github.com/pubsoft-cn/pubsoft/blob/main/src/security/PubSoft.Security.Redis/README.zh-CN.md)


`PubSoft.Security.Redis` implements the `PubSoft.Security.INonceStore` contract for deployments that validate HMAC requests across multiple application instances. A nonce is reserved with one Redis `SET` using `NX` and a TTL; Redis Cluster routes each key independently. Concurrent attempts for the same scope and nonce produce at most one accepted reservation while the key exists.

The adapter hashes the length-delimited scope and nonce before constructing the Redis key, so raw app IDs/nonces are not stored in key names. Configure a deployment-specific `KeyPrefix` and Redis database. All instances that must share replay protection must use the same Redis cluster, database, and prefix. A single-key Lua operation reads Redis server time and atomically sets the remaining TTL only if the absolute acceptance expiry is still in the future; synchronized clocks across application and Redis hosts remain an operational requirement.

## Registration

Register the adapter with the host-managed shared multiplexer:

```csharp
services.AddPubSoftRedisNonceStore(connection, options =>
{
    options.KeyPrefix = "production:edge-a:hmac-nonce:";
});
```

## Refresh Token Families

`AddPubSoftRedisRefreshTokenFamilyStore` registers the shared `IRefreshTokenFamilyStore` for multi-instance refresh-token rotation. Token records are hashes under `{KeyPrefix}{f:familyId}:tok:{sha256}`; the family revocation marker shares the `{f:familyId}` hash tag, so the consume script checks revocation and flips `used` in one atomic single-slot step — safe on Redis Cluster. A `tokptr:` lookup key resolves hash → familyId, and `sub:` sets index subject → familyIds for `RevokeSubjectAsync`. The same durability caveat applies as for nonces: acknowledged writes lost to Redis failover can resurrect a consumed token or drop a revocation marker — tune persistence/replication to the threat model. Pair with `AddPubSoftRefreshTokenFamilyService` in `PubSoft.Security`.

## Token Denylist

`AddPubSoftRedisTokenDenylist` registers the shared `ITokenDenylist` — the revocation source of truth for multi-instance validators. Each deny entry is a hash key under `{KeyPrefix}{tokenId}` that expires at the denied token's own expiry, so the list self-cleans. The durability caveat applies again: a Redis failover that loses acknowledged writes can resurrect a revoked token until its TTL ends. Pair with `PubSoftJwtBearerOptions.CheckDenylist` in `PubSoft.Authentication.Jwt.AspNetCore` for the per-request check.

## Operational Boundaries

Redis is part of the request authorization path. Connection errors propagate; the adapter does not fall back to `MemoryNonceStore`. Configure the multiplexer with the deployment's authentication, TLS, timeouts, and network policy. Cancellation stops waiting for the Redis operation but cannot roll back a command already sent; a canceled reservation may therefore consume that nonce until expiry, which is fail-closed for replay protection.

The Redis provider gives atomicity for one nonce key, not durable persistence across Redis data loss or failover configurations that can lose acknowledged writes. Select persistence/replication settings appropriate to the threat model and validate failover behavior operationally. The in-memory store remains suitable only for single-process use or tests.

The integration test uses two independent clients against a three-primary Redis Cluster and verifies one winner under concurrent reservation, scope separation, and expiry/reuse. This does not replace deployment-specific outage/failover validation.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Security.Redis`: `RedisNonceStore` `RedisNonceStoreOptions` `RedisNonceStoreServiceCollectionExtensions`

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**Registration**

| Method |
|---|---|
| `AddPubSoftRedisNonceStore` |
| `AddPubSoftRedisRefreshTokenFamilyStore` |
| `AddPubSoftRedisTokenDenylist` |

**(options & records)**

`Database`, `KeyPrefix`, `MaxNonceLength`, `MaxScopeLength`, `MaxSubjectLength`, `MaxTokenIdLength`

<!-- pubsoft:api-surface:end -->

