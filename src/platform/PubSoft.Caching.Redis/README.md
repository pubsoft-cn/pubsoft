# PubSoft.Caching.Redis

**English** | [简体中文](README.zh-CN.md)

StackExchange.Redis-backed implementations of the `PubSoft.Caching` contracts. Requires a host-supplied `IConnectionMultiplexer`.

## Cache

```csharp
services.AddPubSoftRedisCache(options =>
{
    options.KeyPrefix = "myapp:";          // key namespace
    options.SerializerOptions = ctx;        // host source-gen STJ context
});
```

`GetOrSetAsync` on a miss runs the factory then writes — concurrent callers and processes can all run it; duplicate work is prevented only by application idempotency or a distributed lock. `RemoveByPatternAsync` scans connected primary endpoints in batches — prefer explicit key lists for routine invalidation. Batch APIs are not transactional.

## Distributed lock

```csharp
services.AddPubSoftRedisDistributedLock(options => options.KeyPrefix = "myapp:locks:");

await using var lease = await distributedLock.AcquireAsync("order:42", TimeSpan.FromSeconds(30));
if (lease is null) return;                          // contended
var fencing = ((IFencedLockHandle)lease).FencingToken; // pass to the protected resource
```

- Acquire + fencing `INCR` in a single Lua step; hash tags keep both keys in one Cluster slot
- Release is owner-token compare-and-delete — a stale holder cannot delete a re-acquired lock
- `ExtendAsync` is explicit; **no automatic renewal**. Pick a lease that covers the operation
- The lease is a duration bound, not a correctness guarantee — downstream resources must validate the monotonically increasing fencing token

For single-process coordination use the in-process lock in `PubSoft.Caching` instead — see [its README](../../platform/PubSoft.Caching/README.md).
