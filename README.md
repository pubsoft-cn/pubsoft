# PubSoft

**English** | [简体中文](README.zh-CN.md)

Open-source infrastructure building blocks for .NET 10 microservices — tenancy, permissions, auditing, messaging, caching, security, scheduling, and resilience, packaged as independent `PubSoft.*` libraries that each do one thing honestly. Optional `PubSoft.Abp.*` adapters integrate the ABP module lifecycle; the generic core never depends on ABP.

## Packages

Per-package hosting guides ship inside every nupkg and live at `src/*/PubSoft.*/README.md`.

| Layer | Packages |
|---|---|
| Foundation | [Core](src/core/PubSoft.Core/README.md) · [Domain](src/core/PubSoft.Domain/README.md) · [Contracts](src/core/PubSoft.Contracts/README.md) · [Mapping](src/core/PubSoft.Mapping/README.md) · [MultiTenancy](src/core/PubSoft.MultiTenancy/README.md) · [Identity](src/core/PubSoft.Identity/README.md) · [Testing](src/testing/PubSoft.Testing/README.md) |
| Data | [EntityFrameworkCore](src/data/PubSoft.EntityFrameworkCore/README.md) |
| Auth | [Authentication](src/security/PubSoft.Authentication/README.md) · [Authentication.Jwt](src/security/PubSoft.Authentication.Jwt/README.md) · [Jwt.AspNetCore](src/security/PubSoft.Authentication.Jwt.AspNetCore/README.md) · [OAuthProviders](src/security/PubSoft.Authentication.OAuthProviders/README.md) · [Authorization](src/security/PubSoft.Authorization/README.md) |
| Messaging & audit | [Messaging](src/messaging/PubSoft.Messaging/README.md) · [Messaging.Outbox](src/messaging/PubSoft.Messaging.Outbox/README.md) · [Auditing](src/data/PubSoft.Auditing/README.md) · [Auditing.Outbox](src/data/PubSoft.Auditing.Outbox/README.md) · [Events](src/core/PubSoft.Events/README.md) |
| Caching & security | [Caching](src/platform/PubSoft.Caching/README.md) · [Caching.Redis](src/platform/PubSoft.Caching.Redis/README.md) · [Security](src/security/PubSoft.Security/README.md) · [Security.Redis](src/security/PubSoft.Security.Redis/README.md) · [Cryptography](src/core/PubSoft.Cryptography/README.md) |
| Runtime | [AspNetCore](src/host/PubSoft.AspNetCore/README.md) · [AspNetCore.Docs](src/host/PubSoft.AspNetCore.Docs/README.md) · [Resilience](src/core/PubSoft.Resilience/README.md) · [Resilience.Http](src/platform/PubSoft.Resilience.Http/README.md) · [Scheduling](src/platform/PubSoft.Scheduling/README.md) · [Scheduling.EF](src/platform/PubSoft.Scheduling.EntityFrameworkCore/README.md) · [Scheduling.Http](src/platform/PubSoft.Scheduling.Http/README.md) · [Monitoring](src/platform/PubSoft.Monitoring/README.md) · [Monitoring.Scheduling](src/platform/PubSoft.Monitoring.Scheduling/README.md) · [Logging](src/core/PubSoft.Logging/README.md) · [Notifications](src/platform/PubSoft.Notifications/README.md) · [Tokens](src/platform/PubSoft.Tokens/README.md) · [Outbound](src/platform/PubSoft.Outbound/README.md) |
| Client entry | [Edge](src/host/PubSoft.Edge/README.md) · [Edge.Core](src/host/PubSoft.Edge.Core/README.md) · [Edge.Client](src/host/PubSoft.Edge.Client/README.md) |
| ABP adapters | [Abp.Core](src/host/PubSoft.Abp.Core/README.md) · [Abp.AspNetCore](src/host/PubSoft.Abp.AspNetCore/README.md) |

## Why PubSoft

Everything is **fail-closed** (undeclared permissions reject, missing verifiers reject, full capacity rejects), **explicit opt-in** (no assembly scanning or implicit registration), and **honest about guarantees** (leases state their expiry bound, Outbox is at-least-once, retention is host-invoked). Full rules: [docs/design-principles.md](docs/design-principles.md).

## Quick start

```bash
dotnet add package PubSoft.Authentication.Jwt
dotnet add package PubSoft.EntityFrameworkCore
```

```csharp
// Issue + validate an RS256 access token (explicit algorithm choice — never a fallback)
using var key = JwtSigning.CreateRsaKey("key-1");
var token = JwtSigning.CreateAccessToken(key, "issuer", "api", new Dictionary<string, string> { ["sub"] = "42" }, TimeSpan.FromMinutes(30));
var parsed = JwtSigning.ValidateAccessToken(token, "issuer", "api", key);

// Named tenant/soft-delete filters on a host DbContext
public sealed class AppDbContext(DbContextOptions<AppDbContext> o, ITenantContext t)
    : PubSoftDbContext(o, t) { }
```

## Documentation

- [docs/](docs/README.md) — cross-package design specs: design principles, packaging/release engineering, AOT feasibility, security model, permission client-scope
- `src/*/PubSoft.*/README.md` — per-package hosting guides (also rendered on NuGet.org package pages)

## Verification

```bash
dotnet test PubSoft.sln -c Release && dotnet pack PubSoft.sln -c Release
bash scripts/verify-package-consumer.sh && bash scripts/verify-aot-consumer.sh
```

Set `PUBSOFT_REDIS`/`PUBSOFT_MYSQL` so integration cases actually run (they skip otherwise); the verify scripts exercise isolated consumers, the pack-time nuspec self-check, and the native AOT smoke. Release engineering details: [docs/packaging.md](docs/packaging.md).

## License & contact

[MIT](LICENSE) · Security issues: **pubsoft@gmail.com** (see [SECURITY.md](SECURITY.md))
