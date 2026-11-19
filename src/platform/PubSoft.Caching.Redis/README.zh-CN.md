---
purpose: PubSoft.Caching.Redis 包级接入指南
audience: 集成方宿主开发者
owner: PubSoft 维护组
last-reviewed: 2026-10-13
---

# PubSoft.Caching.Redis

[English](README.md) | **简体中文**

`PubSoft.Caching` 契约的 StackExchange.Redis 实现。需宿主提供 `IConnectionMultiplexer`。

## 缓存

```csharp
services.AddPubSoftRedisCache(multiplexer, options =>
{
    options.KeyPrefix = "myapp:";          // key 命名空间
    options.SerializerOptions = ctx;        // 宿主 source-gen STJ context
});
```

也可由配置选择 provider——一次调用替代每个宿主的 `if (redis) ... else ...` 分支：

```csharp
services.AddPubSoftCache(configuration);   // 读取 Caching 节
// Caching:Provider = "memory" | "redis"  （缺失或未知值 fail-closed）
// Caching:Redis:ConnectionString         （redis 必填；另有 Database/KeyPrefix/MaxValueBytes）
// Caching:Memory:MaxSize, Caching:Memory:CleanupIntervalSeconds
```

redis 分支的 multiplexer 由 DI 持有：连接延迟到首次解析、随容器 dispose。先自行注册 `IConnectionMultiplexer` 即可保留宿主所有权——缓存将复用它。

`GetOrSetAsync` miss 时执行 factory 再写入——并发调用方与多进程都可能执行；防重复只能靠应用幂等或分布式锁。`RemoveByPatternAsync` 扫描已连接 primary 端点分批删除——日常失效优先显式 key 列表。批量 API 非事务。

## 分布式锁

```csharp
services.AddPubSoftRedisDistributedLock(multiplexer, options => options.KeyPrefix = "myapp:locks:");

`AddPubSoftRedisCheck()` 为 `IHealthChecksBuilder` 加 `redis` 健康检查（PING + 延迟）——EF 健康检查的 Redis 对应件，用于 `/health` 端点。

await using var lease = await distributedLock.AcquireAsync("order:42", TimeSpan.FromSeconds(30));
if (lease is null) return;                          // 竞争失败
var fencing = ((IFencedLockHandle)lease).FencingToken; // 传给受保护资源
```

- acquire + fencing `INCR` 单条 Lua 原子执行；hash tag 保证 Cluster 同槽
- 释放为 owner-token 比较删除——过期持有者删不掉已重取的锁
- `ExtendAsync` 显式调用，**无自动续期**。lease 时长须覆盖受保护操作
- lease 是时长界而非正确性保证——下游资源须校验单调递增的 fencing token
- fencing 计数器默认持久（`FenceRetention = Timeout.InfiniteTimeSpan`），token 不回退；设有限值可为高基数锁命名空间收敛计数内存，代价是闲置超期后可能重置

单进程协调用 `PubSoft.Caching` 的进程内锁——见[其 README](../../platform/PubSoft.Caching/README.zh-CN.md)。
