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
| `MetadataAddress` | — | OIDC discovery 端点；与 `JwksJson` 互斥 |
| `RequireHttpsMetadata` | `true` | HTTP metadata 仅限隔离开发环境 |
| `BackchannelTimeout` | 15 秒 | 上限 2 分钟；远程拉取走 JwtBearer 缓存 + 未知 kid 刷新 |
| `ClockSkew` | 5 秒 | 可配 0–5 分钟 |
| `NameClaimType` / `RoleClaimType` | `sub` / `role` | 其余不做 claim 重映射 |

## 边界

仅 RS256、`kid` 精确选钥、拒绝 JWK 私钥参数、无算法回退。适配器只验证 token——subject 解析、租户解释与权限检查是独立宿主层。完整参考见 [PubSoft.Authentication.Jwt](../../security/PubSoft.Authentication.Jwt/README.zh-CN.md)。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Authentication.Jwt.AspNetCore`: `JwtBearerAuthenticationExtensions` `PubSoftJwtBearerOptions`
