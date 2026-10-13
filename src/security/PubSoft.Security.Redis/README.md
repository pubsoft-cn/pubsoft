# PubSoft.Security.Redis

**English** | [简体中文](README.zh-CN.md)


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

## Operational Boundaries

Redis is part of the request authorization path. Connection errors propagate; the adapter does not fall back to `MemoryNonceStore`. Configure the multiplexer with the deployment's authentication, TLS, timeouts, and network policy. Cancellation stops waiting for the Redis operation but cannot roll back a command already sent; a canceled reservation may therefore consume that nonce until expiry, which is fail-closed for replay protection.

The Redis provider gives atomicity for one nonce key, not durable persistence across Redis data loss or failover configurations that can lose acknowledged writes. Select persistence/replication settings appropriate to the threat model and validate failover behavior operationally. The in-memory store remains suitable only for single-process use or tests.

The integration test uses two independent clients against a three-primary Redis Cluster and verifies one winner under concurrent reservation, scope separation, and expiry/reuse. This does not replace deployment-specific outage/failover validation.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Security.Redis`: `RedisNonceStore` `RedisNonceStoreOptions` `RedisNonceStoreServiceCollectionExtensions`
