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

## 边界

无效输入、容量耗尽、存储不可用一律 fail closed。密钥与凭据不进日志和异常。ASP.NET Core 中间件面（HMAC 中间件、IP 限流、安全头）在 `PubSoft.AspNetCore`。安全模型细节见[安全与密码学](../../../docs/security-cryptography.zh-CN.md)。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Security`: `DataMaskingService` `HmacSignatureValidator` `INonceStore` `MemoryNonceStore` `PasswordHasher` `PasswordVerification` `RateLimiter` `RateLimiterOptions` `RateLimitResult`
