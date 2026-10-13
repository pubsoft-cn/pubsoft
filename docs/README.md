# PubSoft Documentation

**English** | [简体中文](README.zh-CN.md)

`docs/` holds **cross-package**, framework-level design documents. Per-package hosting guides (registration, configuration, boundaries) live next to their package in `src/*/PubSoft.*/README.md` and ship inside each NuGet package.

## Framework-level docs

- [Design Principles](design-principles.md) — fail-closed defaults, explicit opt-in, boundedness, inversion-of-control contracts, ambient scope rules, AOT policy, package layering
- [Packaging & Release Engineering](packaging.md) — versioning, Public API baselines, dependency allowlists, isolated-consumer verification, AOT smoke, release gates
- [Native AOT Feasibility](aot-feasibility.md) — upstream support surface, annotated packages, verified publish results
- [Security Model](security-cryptography.md) — Security/Cryptography/AspNetCore package boundaries, AEAD envelope format, nonce/HMAC semantics
- [Permission Client Scope](permission-client-scope.md) — `ClientCode` authorization namespace semantics across Authorization/EF/ASP.NET

## Package guide index

| Layer | Packages |
|---|---|
| Foundation | [Core](../src/core/PubSoft.Core/README.md) · [Domain](../src/core/PubSoft.Domain/README.md) · [Contracts](../src/core/PubSoft.Contracts/README.md) |
| Identity & auth | [Authentication](../src/security/PubSoft.Authentication/README.md) · [Authentication.Jwt](../src/security/PubSoft.Authentication.Jwt/README.md) ([bearer](../src/security/PubSoft.Authentication.Jwt.AspNetCore/README.md)) · [OAuthProviders](../src/security/PubSoft.Authentication.OAuthProviders/README.md) · [Identity](../src/core/PubSoft.Identity/README.md) · [Authorization](../src/security/PubSoft.Authorization/README.md) · [MultiTenancy](../src/core/PubSoft.MultiTenancy/README.md) |
| Data | [EntityFrameworkCore](../src/data/PubSoft.EntityFrameworkCore/README.md) · [Mapping](../src/core/PubSoft.Mapping/README.md) |
| Messaging & audit | [Messaging](../src/messaging/PubSoft.Messaging/README.md) · [Messaging.Outbox](../src/messaging/PubSoft.Messaging.Outbox/README.md) · [Auditing](../src/data/PubSoft.Auditing/README.md) · [Auditing.Outbox](../src/data/PubSoft.Auditing.Outbox/README.md) · [Events](../src/core/PubSoft.Events/README.md) |
| Caching & security | [Caching](../src/platform/PubSoft.Caching/README.md) · [Caching.Redis](../src/platform/PubSoft.Caching.Redis/README.md) · [Security](../src/security/PubSoft.Security/README.md) · [Security.Redis](../src/security/PubSoft.Security.Redis/README.md) · [Cryptography](../src/core/PubSoft.Cryptography/README.md) |
| Runtime | [AspNetCore](../src/host/PubSoft.AspNetCore/README.md) · [Resilience](../src/core/PubSoft.Resilience/README.md) · [Resilience.Http](../src/platform/PubSoft.Resilience.Http/README.md) · [Scheduling](../src/platform/PubSoft.Scheduling/README.md) ([EF](../src/platform/PubSoft.Scheduling.EntityFrameworkCore/README.md) · [Http](../src/platform/PubSoft.Scheduling.Http/README.md)) · [Monitoring](../src/platform/PubSoft.Monitoring/README.md) ([Scheduling](../src/platform/PubSoft.Monitoring.Scheduling/README.md)) · [Logging](../src/core/PubSoft.Logging/README.md) · [Notifications](../src/platform/PubSoft.Notifications/README.md) · [Tokens](../src/platform/PubSoft.Tokens/README.md) · [Outbound](../src/platform/PubSoft.Outbound/README.md) |
| ABP adapters | [Abp.Core](../src/host/PubSoft.Abp.Core/README.md) · [Abp.AspNetCore](../src/host/PubSoft.Abp.AspNetCore/README.md) |
| Testing | [Testing](../src/testing/PubSoft.Testing/README.md) |
