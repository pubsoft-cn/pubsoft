---
purpose: PubSoft.Security.Redis 包级接入指南
audience: 集成方宿主开发者
owner: PubSoft 维护组
last-reviewed: 2026-10-13
---

# PubSoft.Security.Redis

[English](README.md) | **简体中文**

`PubSoft.Security.Redis` 实现 `PubSoft.Security.INonceStore` 契约，用于多实例部署的 HMAC 请求验证。nonce 以单条 Redis `SET NX` + TTL 原子占用；Redis Cluster 下各 key 独立路由。同 scope+nonce 的并发占用在 key 存活期内至多成功一次。

适配器在构造 Redis key 前对长度分隔的 scope+nonce 做哈希——原始 app ID/nonce 不进 key 名。配置部署专属的 `KeyPrefix` 与 Redis database；所有需共享重放保护的实例必须用同一 Redis cluster、同一 database、同一前缀。单条 Lua 读取 Redis 服务器时间并仅在绝对接受窗口尚未过期时原子设置剩余 TTL——应用与 Redis 主机的时钟同步仍是运维要求。

## 注册

以宿主管控的共享 multiplexer 注册：

```csharp
services.AddPubSoftRedisNonceStore(connection, options =>
{
    options.KeyPrefix = "production:edge-a:hmac-nonce:";
});
```

## 运维边界

Redis 位于请求授权路径上——连接错误直接传播，不回退 `MemoryNonceStore`。按部署配置 multiplexer 的认证、TLS、超时与网络策略。取消只停止等待 Redis 操作，无法回滚已发出的命令——被取消的占用可能仍消耗该 nonce 直至过期，对防重放而言这是 fail-closed。

Redis 提供的是单 nonce key 的原子性，不是跨 Redis 数据丢失或可能丢失已确认写入的 failover 配置的持久保证。按威胁模型选择持久化/复制设置并在运维中验证 failover 行为。内存 store 只适用于单进程或测试。

集成测试用两个独立客户端对三主 Redis Cluster 验证并发占用唯一胜者、scope 隔离与过期/复用——不替代部署专属的故障转移验证。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Security.Redis`: `RedisNonceStore` `RedisNonceStoreOptions` `RedisNonceStoreServiceCollectionExtensions`
