---
purpose: PubSoft.Authentication.Jwt 包级接入指南
audience: 集成方宿主开发者
owner: PubSoft 维护组
last-reviewed: 2026-10-13
---

# PubSoft.Authentication.Jwt

[English](README.md) | **简体中文**

`PubSoft.Authentication.Jwt.AspNetCore` 验证可信身份提供方签发的 bearer token。不签发 token、不存储签名密钥、不把身份映射为应用用户、不选择租户、不定义授权策略。

## 静态公钥 JWKS

宿主把签发方公钥以配置部署时用此模式。issuer/audience 按环境配置；私钥只属于签发方。

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;
using PubSoft.Authentication.Jwt.AspNetCore;

var issuer = builder.Configuration["Authentication:Issuer"]
    ?? throw new InvalidOperationException("Authentication:Issuer is required.");
var audience = builder.Configuration["Authentication:Audience"]
    ?? throw new InvalidOperationException("Authentication:Audience is required.");
var publicJwks = builder.Configuration["Authentication:PublicJwks"]
    ?? throw new InvalidOperationException("Authentication:PublicJwks is required.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddPubSoftJwtBearer(options =>
    {
        options.Issuer = issuer;
        options.Audience = audience;
        options.JwksJson = publicJwks;
    });
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
```

## 远程 OIDC Metadata

签发方暴露 OIDC discovery 文档时，改用 metadata 地址——适配器复用 JwtBearer configuration manager 的缓存与未知 kid 刷新：

```csharp
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddPubSoftJwtBearer(options =>
    {
        options.Issuer = issuer;
        options.Audience = audience;
        options.MetadataAddress = "https://identity.example/.well-known/openid-configuration";
        options.BackchannelTimeout = TimeSpan.FromSeconds(15);
    });
```

远程 metadata 默认要求 HTTPS；关闭 `RequireHttpsMetadata` 允许 HTTP metadata，仅限隔离开发环境。metadata 拉取使用 JwtBearer configuration manager（含缓存与未知 kid 刷新）。确保宿主网络策略只允许目标身份提供方端点。

## 本地签发与验证

`PubSoft.Authentication.Jwt` 还提供独立于 bearer 适配器的签发侧原语：

```csharp
// 非对称（RS256）
using var key = JwtSigning.CreateRsaKey("key-1");
var token = JwtSigning.CreateAccessToken(key, issuer, audience, claims, TimeSpan.FromMinutes(30));
var parsed = JwtSigning.ValidateAccessToken(token, issuer, audience, key);
var jwks = JwtSigning.CreatePublicJwks(key);

// 对称（HS256）——显式选择，绝非回退
using var sym = JwtSigning.CreateSymmetricKey("sym-1");
var hsToken = JwtSigning.CreateAccessToken(sym, issuer, audience, claims, TimeSpan.FromMinutes(30));
var hsParsed = JwtSigning.ValidateAccessToken(hsToken, issuer, audience, sym);
// 或按 kid 从密钥集合解析：
// JwtSigning.ValidateAccessToken(hsToken, issuer, audience, keyA, keyB, keyC);
```

`SymmetricSigningKeyMaterial` 要求至少 256-bit 密钥材料（`CreateSymmetricKey` 生成 256–1024-bit 随机密钥；`ImportSymmetricKey` 接受字节或文本）。签名算法按重载族固定——RSA 重载只收发 RS256，对称重载只收发 HS256——RS256 验证的端点不能被降级成 HS256 token，反之亦然。验证强制精确 issuer/audience/有效期/`kid`；无自动算法回退、无密钥轮换猜测。共享密钥的派生（master/client 密钥层级）归宿主职责。

## 验证与 Claims

适配器要求带过期时间的签名 token，验证精确 issuer、audience、有效期与签名密钥。只接受 ≥2048 位 RSA 密钥与 RS256 token，按 `kid` 精确选一把钥匙，拒绝 JWK 私钥参数，不回退到其他密钥或算法。静态 JWKS 上限 256 KiB、1–64 把唯一公钥。

入站 claim 映射已禁用。默认 name claim 为 `sub`、role claim 为 `role`；宿主可设 `NameClaimType`/`RoleClaimType` 匹配其签发方——只影响 claim 在 principal 上的呈现。适配器不创建授权策略、不解释租户 claim、不授权租户切换；授权与可信租户选择在宿主单独配置。

默认时钟偏移 5 秒，可配 0–5 分钟。远程 metadata backchannel 超时默认 15 秒、上限 2 分钟。静态 JWKS 在认证配置时解析；公钥更新经宿主配置滚动下发。远程 OIDC 模式遇未知 kid 由框架 configuration manager 刷新 metadata。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Authentication.Jwt`: `JwtJwksDocument` `JwtJwksKey` `JwtSigning` `RefreshTokenGenerator` `RsaSigningKeyMaterial` `SymmetricSigningKeyMaterial`
## 验证

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~Jwt
bash scripts/verify-package-consumer.sh
```
