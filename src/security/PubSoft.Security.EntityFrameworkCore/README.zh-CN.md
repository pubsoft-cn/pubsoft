---
purpose: PubSoft.Security refresh 轮换与吊销态的可选 EF Core 持久化
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-11-11
---

# PubSoft.Security.EntityFrameworkCore

基于宿主 DbContext 的 `IRefreshTokenFamilyStore` 与 `ITokenDenylist` 持久化实现——面向"重启不能丢轮换/吊销态、且（暂时）没有 Redis"的部署。

## 范围与保障

- refresh consume 路径是单条原子条件 `UPDATE`（同一语句内 `NOT EXISTS` 查吊销表）——跨实例并发消费恰好只有一个胜者，与 `RedisRefreshTokenFamilyStore` 的 Lua 同语义。
- 吊销族写入标记行；consume 更新在同一语句内复查——吊销永远赢竞态。
- denylist 只存 token id（哈希）不存原 token；`IsDeniedAsync` 仅匹配未过期条目。
- fail-closed 边界：token id/hash、subject、reason 在落库前做长度校验；唯一键冲突抛 `InvalidOperationException` 不静默覆盖。

## 接入

```csharp
// 宿主 DbContext
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ConfigurePubSoftSecurityState();
}

// 注册（宿主 DbContext 与 IIdGenerator 须先注册）
services.AddDbContext<AppDbContext>(/* provider */);
services.AddSingleton<IIdGenerator>(new SnowflakeIdGenerator(workerId));
services.AddPubSoftEfCoreRefreshTokenFamilyStore<AppDbContext>();
services.AddPubSoftEfCoreTokenDenylist<AppDbContext>();
services.AddPubSoftRefreshTokenFamilyService();
```

## 选项

无独立 options——边界在各契约自身 options 内（`RefreshTokenFamilyOptions`）。实体列长：`TokenHash`/`FamilyId` 64、`TokenId` 128、`Subject`/`Reason` 256。

## 宿主侧剩余职责

- schema 归宿主：在 `OnModelCreating` 调 `ConfigurePubSoftSecurityState` 并自建迁移——包不自动建表。
- 保留清理：过期行不自动删除（不同于 Redis TTL）——自行调度两个 store 的 `PruneExpiredAsync`。
- 原子 consume 依赖 provider 默认隔离级下的单条 `UPDATE ... WHERE`，不依赖显式事务；宿主重试策略应按普通写操作对待。
- 多实例横向扩展时 `PubSoft.Security.Redis` 仍是更低延迟的共享 store——EF 是"重启不丢"的选项，不是高吞吐选项。

## Public API surface

<!-- pubsoft:api-surface:start -->
_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._
<!-- pubsoft:api-surface:end -->
