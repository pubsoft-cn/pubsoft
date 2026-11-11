---
purpose: package-level hosting guide for PubSoft.Authentication.Jwt.AspNetCore
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Authentication.Jwt.AspNetCore

**English** | [简体中文](https://github.com/pubsoft-cn/pubsoft/blob/main/src/security/PubSoft.Authentication.Jwt.AspNetCore/README.zh-CN.md)

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
| `CheckDenylist` | `false` | opt-in revocation check: hashes the raw token (SHA-256) against the registered `ITokenDenylist` (`PubSoft.Security`) on every validated request; requires the denylist registered **before** `AddPubSoftJwtBearer` — fails closed at registration and when the denylist is unreachable per request |

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

## Hosted JWKS Endpoint

Issuer hosts publish their key ring's public parameters with one mapping:

```csharp
builder.Services.AddPubSoftJwtIssuerKeyRing(builder.Configuration);
var app = builder.Build();
app.MapPubSoftJwks(); // serves GET /.well-known/jwks.json
```

The document is serialized once at map time — rollover takes effect on redeploy of the ring configuration. Mapping without a registered `IJwtIssuerKeyRing` throws at startup. The endpoint is anonymous by design and never emits private JWK parameters. `cacheMaxAgeSeconds` (default 60, range 0–86400) sets the response `Cache-Control` max-age; keep it short enough that consumers pick up rotated keys promptly.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Authentication.Jwt.AspNetCore`: `DualJwtAuthenticationExtensions` `DualJwtOptions` `JwksEndpointRouteBuilderExtensions` `JwksKeyProvider` `JwksOptions` `JwtBearerAuthenticationExtensions` `PubSoftJwtBearerOptions`

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**Registration**

| Method |
|---|---|
| `AddPubSoftDualJwtAuthentication` |
| `AddPubSoftJwtBearer` |
| `MapPubSoftJwks` |

**(options & records)**

`AllowInsecureJwksUri`, `Audience`, `BackchannelTimeout`, `CacheMinutes`, `CheckDenylist`, `ClientIdClaimType`, `ClockSkew`, `Issuer`, `JwksJson`, `JwksUri`, `MetadataAddress`, `NameClaimType`, `RequireHttpsMetadata`, `RoleClaimType`, `SecretKey`, `SymmetricKey`, `SymmetricKeyId`

<!-- pubsoft:api-surface:end -->

