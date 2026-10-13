# PubSoft.Authentication.Jwt.AspNetCore

**English** | [简体中文](README.zh-CN.md)

`JwtBearer` adapter validating tokens issued by `JwtSigning` or compatible RS256 issuers.

## Registration

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

## Key Options

| Option | Default | Notes |
|---|---|---|
| `Issuer` / `Audience` | required | exact-match validation |
| `JwksJson` | — | static public JWKS (≤256 KiB, 1–64 unique RSA keys) |
| `MetadataAddress` | — | OIDC discovery endpoint; mutually exclusive with `JwksJson`/`SymmetricKey` |
| `SymmetricKey` | — | raw bytes, min 256-bit; HS256 mode for shared-secret issuers; mutually exclusive with the other two sources |
| `SymmetricKeyId` | — | optional `kid` enforcement; missing = single-key accept |
| `RequireHttpsMetadata` | `true` | HTTP metadata only for isolated dev environments |
| `BackchannelTimeout` | 15 s | bounded ≤ 2 min; remote fetch uses JwtBearer's cache + unknown-kid refresh |
| `ClockSkew` | 5 s | configurable 0–5 min |
| `NameClaimType` / `RoleClaimType` | `sub` / `role` | claim remapping is disabled otherwise |

## Boundaries

Two key-source families, mutually exclusive per scheme: **asymmetric** (static JWKS or OIDC metadata — RS256 only, exact `kid`, rejects JWK private-key params) and **symmetric** (`SymmetricKey` — HS256 only, min 256-bit, optional `SymmetricKeyId` enforcement for shared-secret issuers). No algorithm fallback in either mode. The adapter only validates tokens — subject resolution, tenant interpretation, and permission checks are separate host layers. Full reference: [PubSoft.Authentication.Jwt](../../security/PubSoft.Authentication.Jwt/README.md).

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Authentication.Jwt.AspNetCore`: `JwtBearerAuthenticationExtensions` `PubSoftJwtBearerOptions`
