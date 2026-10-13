# PubSoft.Caching

[English](README.md) | **简体中文**

`PubSoft.Caching` 定义 provider 中立的 `ICache` 契约。内置 memory provider 与可选 Redis provider 共享基本的 get/set、TTL、批量与 pattern 删除操作，但并发与持久化语义不同。两者都只是数据缓存——分布式协调使用独立的 `IDistributedLock` 契约，见[分布式锁](#分布式锁)。

## 进程内缓存

数据不需跨重启存活或跨实例共享时注册进程内缓存：

```csharp
using PubSoft.Caching;

services.AddPubSoftMemoryCache(
    cleanupInterval: TimeSpan.FromMinutes(2),
    maxSize: 10_000);
```

`maxSize` 为条目数上限；满时按 LRU 驱逐。过期条目立即不可见并由周期清理移除。`GetOrSetAsync` 仅在本缓存实例内合并同 key 的并发 factory 调用——不是跨进程锁，进程退出时值消失。

## Redis 缓存

宿主持有 `IConnectionMultiplexer` 生命周期并供给可选 Redis 包：

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

适配器以 JSON 存储值，支持可选 database 与 key prefix，遵循 Redis TTL。共享同一 Redis 库的应用与环境须用前缀隔离。`AddPubSoftRedisCache` 替换已注册的 `ICache`，`AddPubSoftMemoryCache` 用 `TryAdd`——组合 provider 时注意注册顺序。

Redis `GetOrSetAsync` 先读缓存，miss 时执行 factory 再写入——并发调用方与多进程都可能执行 factory。重复 factory 执行不安全时须用应用幂等或独立协调机制。

## Key、TTL 与批量

使用带命名空间、有界的 key 与正 TTL；缺 TTL 即不过期。`GetManyAsync` 返回每个 key 的值或 null；`SetManyAsync`/`RemoveManyAsync` 批量处理但非事务——取消或 provider 失败可能留下部分结果。值为类实例，Redis 侧 JSON 序列化；serializer 可经 `RedisCacheOptions.SerializerOptions` 配置。

`RemoveByPatternAsync` 使用 glob 风格 `*`/`?` 匹配。Redis 侧扫描已连接的 primary 端点并分批删除——宽 pattern 开销大，日常失效优先用显式 key 列表或窄前缀。memory provider 对本地 key 使用等价的转义 glob 语义。

## 分布式锁

`IDistributedLock` 获取命名 lease 并返回 handle，dispose 即释放。两个实现：

**`AddPubSoftInProcessDistributedLock`**（`PubSoft.Caching`）——keyed `SemaphoreSlim` 映射，有界容量；lease 到期自动失效（竞争的 acquire 可强制释放过期持有者），handle 可查询当前是否仍持有。仅适合单进程协调。

**`AddPubSoftRedisDistributedLock`**（`PubSoft.Caching.Redis`）——跨进程锁：acquire 与 fencing `INCR` 单条 Lua 原子执行（hash tag 保证 Cluster 下两 key 同槽）；释放为 owner-token 比较删除——过期持有者无法删除已重取的锁；`ExtendAsync` 显式调用——无自动续期。`IFencedLockHandle.FencingToken` 为单调递增 token，下游受保护资源须校验；lease 本身是时长界而非正确性保证。

```csharp
// Memory（单进程）
services.AddPubSoftInProcessDistributedLock();

// Redis（跨进程，需 IConnectionMultiplexer）
services.AddPubSoftRedisDistributedLock(options => options.KeyPrefix = "myapp:locks:");
```

两者均无 watchdog 自动续期——lease 时长须覆盖受保护操作，或显式 `ExtendAsync`。关键资源务必校验 fencing token——持有者可能已过期却自以为仍持有锁。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Caching`: `CachingServiceCollectionExtensions` `ICache` `IDistributedLock` `InProcessDistributedLock` `MemoryCache`
- `PubSoft.Caching.MemoryCache`: `CacheStatistics`

## 验证

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~CachingTests
PUBSOFT_REDIS="localhost:6379,abortConnect=false" dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~RedisCacheTests|FullyQualifiedName~RedisDistributedLockTests
bash scripts/verify-package-consumer.sh
```
