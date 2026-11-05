---
purpose: package-level hosting guide for PubSoft.Caching
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Caching

**English** | [简体中文](README.zh-CN.md)


`PubSoft.Caching` defines the provider-neutral `ICache` contract. The built-in memory provider and optional Redis provider share basic get/set, TTL, batch, and pattern-removal operations, but their concurrency and persistence semantics differ. Both providers are data caches only. Distributed coordination uses a separate `IDistributedLock` contract — see [Distributed Locks](#distributed-locks).

## In-Memory Cache

Register the process-local cache when data need not survive restarts or be shared between instances:

```csharp
using PubSoft.Caching;

services.AddPubSoftMemoryCache(
    cleanupInterval: TimeSpan.FromMinutes(2),
    maxSize: 10_000);
```

`maxSize` is an entry-count limit; inserts at capacity evict least-recently-used entries. Expired entries are invisible immediately and removed by periodic cleanup. `GetOrSetAsync` coalesces concurrent factory calls for the same key within this cache instance only. It is not a cross-process lock, and values disappear when the process exits.

## Redis Cache

The host owns the `IConnectionMultiplexer` lifecycle and supplies it to the optional Redis package:

```csharp
using PubSoft.Caching.Redis;
using StackExchange.Redis;

var redis = await ConnectionMultiplexer.ConnectAsync(redisConfiguration);
services.AddSingleton<IConnectionMultiplexer>(redis);
services.AddPubSoftRedisCache(redis, options =>
{
    options.Database = -1;
    options.KeyPrefix = "billing:prod:";
});
```

The adapter stores values as JSON, supports an optional database and key prefix, and honors Redis TTLs. Choose a prefix that isolates applications and environments sharing a Redis database. `AddPubSoftRedisCache` replaces the registered `ICache`, while `AddPubSoftMemoryCache` uses `TryAdd`; registration order matters if composing provider modules.

Redis `GetOrSetAsync` performs a cache read, invokes the factory on a miss, then writes the value. Concurrent callers and multiple processes may all invoke the factory. Use application idempotency or a separately designed coordination mechanism when duplicate factory work is unsafe. `IDistributedLock` locking is described under [Distributed Locks](#distributed-locks).

## Keys, TTL, and Batches

Use namespaced, bounded keys and positive TTLs. A missing TTL means no expiration. `GetManyAsync` returns each requested key with either its value or null; `SetManyAsync` and `RemoveManyAsync` process bounded batches but are not a transaction, so cancellation or a provider failure can leave partial results. Values are class instances and are serialized/deserialized as JSON by Redis; serializer options can be configured with `RedisCacheOptions.SerializerOptions`.

`RemoveByPatternAsync` uses glob-style `*` and `?` matching. In Redis it scans connected primary endpoints and deletes matches in batches; broad patterns can be expensive, so prefer explicit key lists or narrowly scoped prefixes for routine invalidation. The memory provider uses equivalent escaped glob semantics over its local keys.

## Distributed Locks

`IDistributedLock` acquires a named lease and returns a handle released by disposal. Two implementations exist:

**`AddPubSoftInProcessDistributedLock`** (`PubSoft.Caching`) — keyed `SemaphoreSlim` map with bounded capacity; the lease expires on its own timeout (a contended acquire can force-release an expired holder), and handles report whether they still hold the lease. Suitable for single-process coordination only.

**`AddPubSoftRedisDistributedLock`** (`PubSoft.Caching.Redis`) — cross-process lock: acquire and fencing `INCR` run in one Lua step (hash-tagged so Cluster keeps both keys in one slot); release is owner-token compare-and-delete so a stale holder cannot delete a re-acquired lock; `ExtendAsync` is explicit — there is no automatic renewal. `IFencedLockHandle.FencingToken` is a monotonically increasing token that downstream resources should validate for fencing; the lease itself is a duration bound, not a correctness guarantee.

```csharp
// Memory (single-process)
services.AddPubSoftInProcessDistributedLock();

// Redis (cross-process, requires IConnectionMultiplexer)
services.AddPubSoftRedisDistributedLock(options => options.KeyPrefix = "myapp:locks:");
```

Neither implementation provides automatic watch-dog renewal; choose a lease duration that covers the protected operation or call `ExtendAsync` explicitly. For critical resources, always check the fencing token — a lease holder may have expired while it still believed it held the lock.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Caching`: `CachingServiceCollectionExtensions` `ICache` `IDistributedLock` `InProcessDistributedLock` `MemoryCache`
- `PubSoft.Caching.MemoryCache`: `CacheStatistics`

## Verification

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~CachingTests
PUBSOFT_REDIS="localhost:6379,abortConnect=false" dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~RedisCacheTests|FullyQualifiedName~RedisDistributedLockTests
bash scripts/verify-package-consumer.sh
```