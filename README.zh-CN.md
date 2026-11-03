# PubSoft

[English](README.md) | **简体中文**

面向 .NET 10 微服务的开源基础设施构件库——租户、权限、审计、消息、缓存、安全、调度、韧性，以独立的 `PubSoft.*` 包提供，每个包只诚实地做一件事。

## 包清单

每包的宿主指南随 nupkg 发布，源码位于 `src/*/PubSoft.*/README.md`。

| 层 | 包 |
|---|---|
| 基础 | [Core](src/core/PubSoft.Core/README.md) · [Domain](src/core/PubSoft.Domain/README.md) · [Contracts](src/core/PubSoft.Contracts/README.md) · [Mapping](src/core/PubSoft.Mapping/README.md) · [MultiTenancy](src/core/PubSoft.MultiTenancy/README.md) · [Identity](src/core/PubSoft.Identity/README.md) · [Testing](src/testing/PubSoft.Testing/README.md) |
| 数据 | [EntityFrameworkCore](src/data/PubSoft.EntityFrameworkCore/README.md) |
| 认证授权 | [Authentication](src/security/PubSoft.Authentication/README.md) · [Authentication.Jwt](src/security/PubSoft.Authentication.Jwt/README.md) · [Jwt.AspNetCore](src/security/PubSoft.Authentication.Jwt.AspNetCore/README.md) · [OAuthProviders](src/security/PubSoft.Authentication.OAuthProviders/README.md) · [Authorization](src/security/PubSoft.Authorization/README.md) |
| 消息与审计 | [Messaging](src/messaging/PubSoft.Messaging/README.md) · [Messaging.Outbox](src/messaging/PubSoft.Messaging.Outbox/README.md) · [Modularity](src/platform/PubSoft.Modularity/README.md) · [Auditing](src/data/PubSoft.Auditing/README.md) · [Auditing.Outbox](src/data/PubSoft.Auditing.Outbox/README.md) · [Events](src/core/PubSoft.Events/README.md) |
| 缓存与安全 | [Caching](src/platform/PubSoft.Caching/README.md) · [Caching.Redis](src/platform/PubSoft.Caching.Redis/README.md) · [Security](src/security/PubSoft.Security/README.md) · [Security.Redis](src/security/PubSoft.Security.Redis/README.md) · [Cryptography](src/core/PubSoft.Cryptography/README.md) |
| 运行时 | [AspNetCore](src/host/PubSoft.AspNetCore/README.md) · [AspNetCore.Docs](src/host/PubSoft.AspNetCore.Docs/README.md) · [Resilience](src/core/PubSoft.Resilience/README.md) · [Resilience.Http](src/platform/PubSoft.Resilience.Http/README.md) · [Scheduling](src/platform/PubSoft.Scheduling/README.md) · [Scheduling.EF](src/platform/PubSoft.Scheduling.EntityFrameworkCore/README.md) · [Scheduling.Http](src/platform/PubSoft.Scheduling.Http/README.md) · [Monitoring](src/platform/PubSoft.Monitoring/README.md) · [Monitoring.Scheduling](src/platform/PubSoft.Monitoring.Scheduling/README.md) · [Logging](src/core/PubSoft.Logging/README.md) · [Notifications](src/platform/PubSoft.Notifications/README.md) · [Tokens](src/platform/PubSoft.Tokens/README.md) · [Outbound](src/platform/PubSoft.Outbound/README.md) |
| 客户端入口 | [Edge](src/host/PubSoft.Edge/README.zh-CN.md) · [Edge.Core](src/host/PubSoft.Edge.Core/README.zh-CN.md) · [Edge.Client](src/host/PubSoft.Edge.Client/README.zh-CN.md) |

## 为什么选 PubSoft

全部**fail-closed**（未声明权限拒绝、缺校验器拒绝、容量满拒绝）、**显式 opt-in**（无程序集扫描、无隐式注册）、**语义如实**（lease 注明过期边界、Outbox 为 at-least-once、保留期由宿主调用）。完整规范：[docs/design-principles.zh-CN.md](docs/design-principles.zh-CN.md)。

## 快速开始

```bash
dotnet add package PubSoft.Authentication.Jwt
dotnet add package PubSoft.EntityFrameworkCore
```

```csharp
// 签发并验证 RS256 access token（算法显式选择——不存在静默降级）
using var key = JwtSigning.CreateRsaKey("key-1");
var token = JwtSigning.CreateAccessToken(key, "issuer", "api", new Dictionary<string, string> { ["sub"] = "42" }, TimeSpan.FromMinutes(30));
var parsed = JwtSigning.ValidateAccessToken(token, "issuer", "api", key);

// 宿主 DbContext 获得命名租户/软删除过滤器
public sealed class AppDbContext(DbContextOptions<AppDbContext> o, ITenantContext t)
    : PubSoftDbContext(o, t) { }
```

## 文档

- [docs/](docs/README.zh-CN.md)——跨包设计规范：设计原则、打包发布工程、AOT 可行性、安全模型、权限 client-scope
- `src/*/PubSoft.*/README.md`——每包宿主指南（英文，同时显示在 NuGet.org 包页）

## 验证

```bash
dotnet test PubSoft.sln -c Release && dotnet pack PubSoft.sln -c Release
bash scripts/verify-package-consumer.sh && bash scripts/verify-aot-consumer.sh
```

配置 `PUBSOFT_REDIS`/`PUBSOFT_MYSQL` 后集成用例才会真正执行（未配置则跳过）；verify 脚本负责隔离 consumer、pack 期 nuspec 自检与原生 AOT smoke。发布工程细节：[docs/packaging.zh-CN.md](docs/packaging.zh-CN.md)。

## 许可证与联系

[MIT](LICENSE) · 安全问题请私下报告至 **pubsoft@gmail.com**（见 [SECURITY.zh-CN.md](SECURITY.zh-CN.md)）
