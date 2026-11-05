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
services.AddPubSoftRedisCache(options =>
{
    options.KeyPrefix = "myapp:";          // key 命名空间
    options.SerializerOptions = ctx;        // 宿主 source-gen STJ context
});
```

`GetOrSetAsync` miss 时执行 factory 再写入——并发调用方与多进程都可能执行；防重复只能靠应用幂等或分布式锁。`RemoveByPatternAsync` 扫描已连接 primary 端点分批删除——日常失效优先显式 key 列表。批量 API 非事务。

## 分布式锁

```csharp
services.AddPubSoftRedisDistributedLock(options => options.KeyPrefix = "myapp:locks:");

await using var lease = await distributedLock.AcquireAsync("order:42", TimeSpan.FromSeconds(30));
if (lease is null) return;                          // 竞争失败
var fencing = ((IFencedLockHandle)lease).FencingToken; // 传给受保护资源
```

- acquire + fencing `INCR` 单条 Lua 原子执行；hash tag 保证 Cluster 同槽
- 释放为 owner-token 比较删除——过期持有者删不掉已重取的锁
- `ExtendAsync` 显式调用，**无自动续期**。lease 时长须覆盖受保护操作
- lease 是时长界而非正确性保证——下游资源须校验单调递增的 fencing token

单进程协调用 `PubSoft.Caching` 的进程内锁——见[其 README](../../platform/PubSoft.Caching/README.zh-CN.md)。
