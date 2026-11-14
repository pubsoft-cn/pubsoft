---
purpose: package-level hosting guide for PubSoft.Authentication.Jwt
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Authentication.Jwt

**English** | [简体中文](https://github.com/pubsoft-cn/pubsoft/blob/main/src/security/PubSoft.Authentication.Jwt/README.zh-CN.md)


`PubSoft.Authentication.Jwt.AspNetCore` validates bearer tokens issued by a trusted identity provider. It does not issue tokens, store signing secrets, map identities to application users, choose tenants, or define authorization policies.

## Static Public JWKS

Use this mode when the host deploys the issuer's public signing keys as configuration. Keep issuer and audience values environment-specific; private signing keys belong only to the issuer.

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

## Remote OIDC Metadata

Choose remote metadata instead of `JwksJson` when the issuer publishes an OpenID Connect discovery document. The metadata document's issuer must exactly match `Issuer`.

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

Remote metadata requires HTTPS by default. Disabling `RequireHttpsMetadata` permits HTTP metadata and should be limited to isolated development environments. Metadata retrieval uses JwtBearer's configuration manager, including its cache and refresh-on-unknown-key behavior. Ensure the host's network policy allows only the intended identity provider endpoints.

## Local Signing and Validation

`PubSoft.Authentication.Jwt` also provides issuer-side primitives independent of the bearer adapter:

```csharp
// Asymmetric (RS256)
using var key = JwtSigning.CreateRsaKey("key-1");
var token = JwtSigning.CreateAccessToken(key, issuer, audience, claims, TimeSpan.FromMinutes(30));
var parsed = JwtSigning.ValidateAccessToken(token, issuer, audience, key);
var jwks = JwtSigning.ExportJwks(key);

// Symmetric (HS256) — explicit choice, never a fallback
using var sym = JwtSigning.CreateSymmetricKey("sym-1");
var hsToken = JwtSigning.CreateAccessToken(sym, issuer, audience, claims, TimeSpan.FromMinutes(30));
var hsParsed = JwtSigning.ValidateAccessToken(hsToken, issuer, audience, sym);
// or resolve by kid from a key set:
// JwtSigning.ValidateAccessToken(hsToken, issuer, audience, keyA, keyB, keyC);
```

`SymmetricSigningKeyMaterial` requires at least 256 bits of key material (`CreateSymmetricKey` generates a random 256–1024-bit key; `ImportSymmetricKey` accepts bytes or text). The signing algorithm is fixed per overload family — the RSA overloads emit and accept only RS256, the symmetric overloads only HS256 — so an RS256-validated endpoint cannot be downgraded to an HS256 token or vice versa. Validation enforces exact issuer, audience, lifetime, and `kid`; there is no automatic algorithm fallback and no key rollover guessing. Derivation of shared secrets (master/client key hierarchies) is a host responsibility.

### Client Assertions

`ClientAssertion.Sign` mints a `private_key_jwt`-shape RS256 JWT (`iss`=`sub`=client id, audience-bound, unique `jti`, lifetime ≤ 5 minutes) for callers authenticating to services that verify assertions. Server-side, the host resolves each client's **public** key through `IClientAssertionKeyProvider` — the registry stores public keys only, so a leaked registry cannot mint assertions. Build public-only material with `JwtSigning.ImportRsaPublicKey`. See `PubSoft.AspNetCore` for the verifier.

`AddPubSoftJwksClientAssertionKeyProvider` is the built-in `IClientAssertionKeyProvider` implementation: a host-injected `FetchJwks` delegate returns each client's JWKS document (RFC 7517), RSA signing keys are cached per client id (`CacheDuration`, default 5 min), misses are negatively cached (`MissCacheDuration`, default 1 min), and expired sets are never served stale — a rotated-out key must stop verifying.

### Issuer Key Ring

Hosts that issue tokens register a configuration-bound ring instead of passing key material around:

```csharp
builder.Services.AddPubSoftJwtIssuerKeyRing(builder.Configuration);
// Jwt:IssuerKeys:ActiveKeyId = "key-2"
// Jwt:IssuerKeys:Keys:key-1 / Keys:key-2 -> RSA private key PEM values
```

`IJwtIssuerKeyRing` exposes `ActiveKey` for issuance (`JwtSigning.CreateAccessToken(ring.ActiveKey, ...)`) and `PublishedKeys` for `JwtSigning.ExportJwks` and multi-key validation. Registration fails closed on an empty ring, a missing active key id, malformed PEM, or keys below 2048 bits. Every configured key is published — keep retiring keys until their issued tokens expire, then remove them. The ring performs no scheduled rotation; generating and rolling PEM material is a host operations concern.

## Validation and Claims

The adapter requires a signed token with an expiration and validates exact issuer, audience, lifetime, and signing key. It accepts only RSA keys of at least 2048 bits and RS256 tokens, selects exactly one key by `kid`, rejects private JWK parameters, and does not fall back to another key or algorithm. Static JWKS is limited to 256 KiB and 1-64 unique public signing keys.

Inbound claim mapping is disabled. The default name claim is `sub` and default role claim is `role`; hosts may set `NameClaimType` and `RoleClaimType` to match their issuer. These settings only define how claims are surfaced by the principal. The adapter does not create authorization policies, interpret tenant claims, or authorize a tenant switch. Configure authorization and trusted tenant selection separately in the host.

The default clock skew is five seconds and can be configured from zero through five minutes. The remote metadata backchannel timeout defaults to 15 seconds and must be no more than two minutes. Static JWKS is parsed when authentication is configured; deploy updated public keys through host configuration rollout. Remote OIDC mode uses the framework configuration manager to refresh metadata when an unknown key is encountered.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Authentication.Jwt`: `ClientAssertion` `ConfigJwtIssuerKeyRing` `IClientAssertionKeyProvider` `IJwtIssuerKeyRing` `JwtIssuerKeyRingOptions` `JwtIssuerKeyRingServiceCollectionExtensions` `JwtJwksDocument` `JwtJwksKey` `JwtSigning` `RefreshTokenGenerator` `IClientSigningKeyProvider` `RsaSigningKeyMaterial` `SymmetricSigningKeyMaterial`

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**Registration**

| Method |
|---|---|
| `AddPubSoftJwtIssuerKeyRing` |

**(options & records)**

`ActiveKeyId`

<!-- pubsoft:api-surface:end -->

## Verification

Run the focused bearer tests with:

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~JwtBearerAuthenticationTests
```

These tests cover static and remote keys, exact key selection, claim-name preservation, private-key rejection, HTTPS defaults, metadata recovery, and cancellation. They do not validate the issuer's key-management process or the host's application authorization rules.