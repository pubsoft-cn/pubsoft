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

## Dual JWT (local HS256 + remote AuthServer JWKS)

`AddPubSoftDualJwtAuthentication(configuration)` registers a second, legacy-style pair of schemes: bearer tokens whose `alg` header is `RS256` are forwarded to `AuthServerJwks`, everything else validates against the local `Jwt:` settings.

| Section | Keys | Behavior |
|---|---|---|
| `Jwt:` | `Issuer`, `Audience`, `SecretKey` | local HS256 bearer — issuer/audience/lifetime/key validated, algorithm pinned to HS256 |
| `AuthServer:` | `Issuer`, `JwksUri`, `Audience`, `CacheMinutes`, `AllowInsecureJwksUri` | remote-JWKS `AuthServerJwks` scheme — algorithm pinned to RS256 |

- `JwksUri` must be an absolute https URI; loopback hosts are permitted for development, other plaintext endpoints require `AllowInsecureJwksUri = true`. `CacheMinutes` must be positive. Both are validated at registration and fail startup.
- Resolved keys live only for the JWKS set's `CacheMinutes` TTL — once the cached set expires and a `kid` is absent from JWKS, tokens signed by it are rejected. An empty JWKS response is cached for the TTL too, so removal responses hold and empty sets are not refetched per token.
- `Audience` is optional on `AuthServer:` — when set, the scheme validates `aud`; when empty it is skipped. Configure it whenever the issuer mints tokens for more than one API.
- `JwksKeyProvider` resolves through the host container when `JwtBearerOptions` are created — singleton, cache and disposal lifetimes follow normal DI rules.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Authentication.Jwt.AspNetCore`: `DualJwtAuthenticationExtensions` `DualJwtOptions` `JwksKeyProvider` `JwksOptions` `JwtBearerAuthenticationExtensions` `PubSoftJwtBearerOptions`
