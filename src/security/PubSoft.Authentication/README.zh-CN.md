---
purpose: PubSoft.Authentication 包级接入指南
audience: 集成方宿主开发者
owner: PubSoft 维护组
last-reviewed: 2026-10-13
---

# PubSoft.Authentication

[English](README.md) | **简体中文**

`PubSoft.Authentication` 提供 `ClaimsPrincipal` 的框架无关不可变快照——复制认证标志与每个 claim 的 type、value、valueType、issuer：

```csharp
using PubSoft.Authentication;
using System.Security.Claims;

ClaimsPrincipal principal = GetPrincipal();
PrincipalSnapshot snapshot = PrincipalSnapshot.FromClaimsPrincipal(principal);

foreach (PrincipalClaim claim in snapshot.Claims)
    Console.WriteLine($"{claim.Type}={claim.Value} ({claim.Issuer})");
```

快照保留跨 identity 的 claim 顺序与重复项，复制为只读集合；任一 identity 已认证则 `IsAuthenticated` 为 true；拒绝 null principal。

## Subject 解析

快照刻意不解释 claim 名与值——不解析 user ID、租户、角色、权限、管理员身份或可信 issuer。消费方须用显式定义的身份/授权契约——裸 claim 本身不构成租户切换或特权操作的授权证明。

已定义权威 claim 映射的应用可用 `AuthenticatedSubjectResolver` 从**一个**精确匹配的可信 issuer 解析 subject claim 与可选 tenant claim：

```csharp
var resolver = new AuthenticatedSubjectResolver(
    subjectClaimType: "sub",
    trustedIssuer: "https://identity.example",
    tenantClaimType: "tenant_id");

AuthenticatedSubject? subject = resolver.Resolve(snapshot);
if (subject is null)
    throw new UnauthorizedAccessException();
```

解析要求：已认证快照、恰好一个匹配的 subject claim（正数 invariant `long`）、至多一个匹配的 tenant claim（存在时也须为正数 `long`）——否则 fail closed。未配置 tenant claim 时 `TenantId=null`。其他 issuer 与 claim 名被忽略。可信 issuer 与 claim type 须按宿主已验证的 token 契约配置——本助手不验签、不授权租户切换、不解析角色、不授权限。

## Scoped Accessor

`IPrincipalSnapshotAccessor` 为框架无关读取契约；可选 `PubSoft.AspNetCore` 注册 scoped accessor，每次调用快照当前 `HttpContext.User`，无请求返回 `null`。不缓存 scope 内快照，不修改 principal。

按请求直接解析已配置 subject 时显式注册附加 accessor：

```csharp
builder.Services.AddAuthenticatedSubjectAccessor(
    subjectClaimType: "sub",
    trustedIssuer: "https://identity.example",
    tenantClaimType: "tenant_id");

var subject = httpContext.RequestServices
    .GetRequiredService<IAuthenticatedSubjectAccessor>()
    .GetCurrent();
```

该注册同时加入 principal snapshot accessor 与单例 resolver。`null` 结果表示无当前请求或映射被拒绝——不等于端点响应码或租户切换结论。认证中间件顺序、issuer 验证与租户选择授权仍归宿主。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Authentication`: `AuthenticatedSubject` `AuthenticatedSubjectResolver` `IAuthenticatedSubjectAccessor` `IPrincipalSnapshotAccessor` `PrincipalClaim` `PrincipalSnapshot`
