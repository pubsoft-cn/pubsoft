# PubSoft.Authentication.Jwt.AspNetCore

[English](README.md) | **简体中文**

`JwtBearer` 适配器，验证 `JwtSigning` 或兼容 RS256 签发方产出的 token。

## 注册

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddPubSoftJwtBearer(options =>
    {
        options.Issuer = "https://identity.example";
        options.Audience = "api";
        options.MetadataAddress = "https://identity.example/.well-known/openid-configuration";
    });
builder.Services.AddAuthorization();
app.UseAuthentication();
app.UseAuthorization();
```

## 关键选项

| 选项 | 默认 | 说明 |
|---|---|---|
| `Issuer` / `Audience` | 必填 | 精确匹配验证 |
| `JwksJson` | — | 静态公钥 JWKS（≤256 KiB、1–64 把唯一 RSA 公钥） |
| `MetadataAddress` | — | OIDC discovery 端点；与 `JwksJson`/`SymmetricKey` 互斥 |
| `SymmetricKey` | — | 原始字节，最小 256-bit；共享密钥签发方用 HS256 模式；与其他两种密钥源互斥 |
| `SymmetricKeyId` | — | 可选 `kid` 强制校验；缺省则单钥放行 |
| `RequireHttpsMetadata` | `true` | HTTP metadata 仅限隔离开发环境 |
| `BackchannelTimeout` | 15 秒 | 上限 2 分钟；远程拉取走 JwtBearer 缓存 + 未知 kid 刷新 |
| `ClockSkew` | 5 秒 | 可配 0–5 分钟 |
| `NameClaimType` / `RoleClaimType` | `sub` / `role` | 其余不做 claim 重映射 |

## 边界

两类密钥源按 scheme 互斥：**非对称**（静态 JWKS 或 OIDC metadata——仅 RS256、`kid` 精确、拒绝 JWK 私钥参数）与**对称**（`SymmetricKey`——仅 HS256、最小 256-bit、可选 `SymmetricKeyId` 强制校验共享密钥签发方）。两种模式均无算法回退。适配器只验证 token——subject 解析、租户解释与权限检查是独立宿主层。完整参考见 [PubSoft.Authentication.Jwt](../../security/PubSoft.Authentication.Jwt/README.zh-CN.md)。

## Dual JWT（本地 HS256 + 远端 AuthServer JWKS）

`AddPubSoftDualJwtAuthentication(configuration)` 注册第二组 scheme：`alg` 头为 `RS256` 的 bearer token 转发到 `AuthServerJwks`，其余按本地 `Jwt:` 配置验证。

| 节 | 键 | 行为 |
|---|---|---|
| `Jwt:` | `Issuer`、`Audience`、`SecretKey` | 本地 HS256 bearer——issuer/audience/有效期/密钥全校验，算法固定 HS256 |
| `AuthServer:` | `Issuer`、`JwksUri`、`Audience`、`CacheMinutes`、`AllowInsecureJwksUri` | 远端 JWKS 的 `AuthServerJwks` scheme——算法固定 RS256 |

- `JwksUri` 必须是绝对 https URI；loopback 主机允许用于开发，其他明文端点须 `AllowInsecureJwksUri = true`。`CacheMinutes` 必须为正。两者在注册期校验，非法即启动失败。
- 已解析 key 的有效期跟随 JWKS 集合的 `CacheMinutes` TTL——缓存集合过期后，JWKS 中已消失的 `kid` 签发的 token 会被拒绝。空 JWKS 响应同样按 TTL 缓存——移除响应保持生效，且空集合不会逐 token 回源。
- `AuthServer:` 的 `Audience` 可选——配置后 scheme 校验 `aud`；为空则跳过。issuer 为多个 API 签发 token 时应配置。
- `JwksKeyProvider` 在创建 `JwtBearerOptions` 时经宿主容器解析——singleton、缓存与释放生命周期遵循常规 DI 规则。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Authentication.Jwt.AspNetCore`: `DualJwtAuthenticationExtensions` `DualJwtOptions` `JwksKeyProvider` `JwksOptions` `JwtBearerAuthenticationExtensions` `PubSoftJwtBearerOptions`
