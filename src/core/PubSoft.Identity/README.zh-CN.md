---
purpose: PubSoft.Identity 包级接入指南
audience: 集成方宿主开发者
owner: PubSoft 维护组
last-reviewed: 2026-10-13
---

# PubSoft.Identity

[English](README.md) | **简体中文**

`PubSoft.Identity` 提供框架无关的用户模型与只读 profile 契约。基础 tenant-aware 用户聚合用 `IdentityUser`；传递不含凭据的 profile 数据用 `IdentityUserSnapshot`。

```csharp
using PubSoft.Identity;

public sealed record UserProfile(
    long Id,
    string UserName,
    string Name,
    string? PhoneNumber) : IIdentityUser;

IIdentityUser profile = new UserProfile(42, "jdoe", "Jane Doe", null);
IdentityUserSnapshot snapshot = IdentityUserSnapshot.FromUser(profile);
```

快照把 `Id`/`UserName`/`Name`/`PhoneNumber` 复制为只读属性；`FromUser` 拒绝 null。契约不定义校验、归一化、唯一性、持久化或生命周期。

## IdentityUser 与持久化

profile 不含凭据——无密码、token、密钥字段；也不确立认证、角色、权限、租户归属或授权策略。这些决策归宿主的身份/授权边界。

`IdentityUser` 继承 `FullAuditedAggregateRoot`：应用分配的正数 ID、可选 `TenantId`、profile 字段、`NormalizedUserName`、可选 `PasswordHash` 与 `SecurityStamp`。用户名经 trim + `ToUpperInvariant` 归一化——提供稳定比较键但不强制唯一性。可选 EF mapping `ConfigurePubSoftIdentityUsers()` 提供 `(TenantId, NormalizedUserName)` 唯一索引并继承 tenant/软删过滤器；非空 TenantId 由索引保证唯一，Host 级（`TenantId=null`）唯一性须按 provider 的 NULL 唯一索引行为显式处理。

`SetPasswordHash` 接受不透明编码哈希而非明文——哈希用 `PubSoft.Security.PasswordHasher` 生成/验证。设置或移除哈希会轮换 `SecurityStamp`；仅为模型更新——宿主须持久化并在 token/session 策略中校验 stamp 才能使既有会话失效。模型刻意不含登录失败计数、锁定时长、外部 OAuth token 存储或登录事务行为。

EF mapping 为 opt-in，不自行建表/迁移；将 `IdentityUser` 加入宿主 DbContext 并应用 `ConfigurePubSoftIdentityUsers()` 后由宿主生成 provider 迁移。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

`IdentityUserLogin` 存外部登录绑定（provider + provider key → user），对齐标准身份模型；`PubSoft.EntityFrameworkCore.ConfigurePubSoftIdentityUserLogins()` 映射为 `(TenantId, LoginProvider, ProviderKey)` 唯一索引 + `UserId` 查询索引 + `ExtJson` 扩展列。

- `PubSoft.Identity`: `IIdentityUser` `IdentityUser` `IdentityUserSnapshot` `IdentityUserLogin`
