---
purpose: PubSoft.Security 包级接入指南
audience: 集成方宿主开发者
owner: PubSoft 维护组
last-reviewed: 2026-10-13
---

# PubSoft.Security

[English](README.md) | **简体中文**

通用安全原语，零包依赖——密码哈希、PII 脱敏、HMAC 防重放、有界进程内限流器。

## 密码哈希

```csharp
var hash = PasswordHasher.Hash(password);                 // PBKDF2-HMAC-SHA256，随机盐
var result = PasswordHasher.VerifyAndRehash(password, storedHash);
if (result.Succeeded && result.ReplacementHash is { } next)
    SaveUpgradedHash(next);                               // 宿主在自己事务中持久化
```

PBKDF2-HMAC-SHA256，随机 16 字节盐、默认 600,000 迭代。`VerifyAndRehash` 同时验证受支持的旧格式（无盐 SHA-256、低工作因子哈希）并返回 `ReplacementHash`——让调用方透明升级存量哈希，与登录状态更新在同一事务持久化。助手不加载用户、不更新 security stamp、不计失败次数。

## PII 脱敏

`DataMaskingService` 为日志/诊断脱敏，可见长度选项经验证。非匿名化，不替代访问控制；配置的可见区间会暴露整个短输入时整体脱敏。

## HMAC 防重放

`HmacSignatureValidator` 验证 `method`/`path+rawQuery`/`timestamp`/`nonce`/`bodyHash` 签名，定时常量比较。nonce 经 `INonceStore` **仅在签名验证通过后**占用；`MemoryNonceStore` 有界且进程内——多实例部署须注册分布式实现（`PubSoft.Security.Redis`）。

## 限流

`RateLimiter` 是有界单进程固定窗口限流器，按标识符原子计数。非集群级——共享配额用网关或经评审的分布式实现。无效标识符与容量耗尽 fail closed。

## Refresh Token 族轮换

`RefreshTokenFamilyService` 在族内轮换不透明 refresh token：每次出示的 token 被原子消费并签发后继，按 SHA-256 哈希键控——原始 token 不落库。出示已消费的 token 视为泄露信号，吊销**整个族**（`RefreshRotationVerdict.ReuseDetected`）；已吊销的族对后续任何轮换 fail-closed。族有独立于单 token `TokenLifetime` 的绝对期限 `FamilyMaxLifetime`；`RevokeSubjectAsync` 吊销某 subject 全部已知族（全端登出）。`AddPubSoftMemoryRefreshTokenFamily` 注册有界进程内 store——仅单实例；多实例部署须注册共享 `IRefreshTokenFamilyStore`（如 `PubSoft.Security.Redis`），且进程内状态不跨重启保留。

## Token 吊销名单

`ITokenDenylist` 是吊销真相源：`DenyAsync` 记录 token id（`TokenDenyIds.ForToken`=原始 token 的 SHA-256，或 jti 等稳定 id）直至该 token 自身过期；`IsDeniedAsync` 响应每次请求的吊销检查。条目随 token 过期自清——名单不会比被吊销的 token 活得更久。`AddPubSoftMemoryTokenDenylist` 注册有界进程内实现——仅单实例；多实例部署须注册共享实现（如 `PubSoft.Security.Redis`）。注意：进程内吊销状态不跨重启——重启后已吊销的 access token 在剩余 TTL 内会复活，除非使用共享/持久化 store。

## 边界

无效输入、容量耗尽、存储不可用一律 fail closed。密钥与凭据不进日志和异常。ASP.NET Core 中间件面（HMAC 中间件、IP 限流、安全头）在 `PubSoft.AspNetCore`。安全模型细节见[安全与密码学](../../../docs/security-cryptography.zh-CN.md)。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Security`: `DataMaskingService` `HmacSignatureValidator` `INonceStore` `MemoryNonceStore` `PasswordHasher` `PasswordVerification` `RateLimiter` `RateLimiterOptions` `RateLimitResult`
