---
purpose: Native AOT feasibility assessment for the package set
audience: contributors and integrators targeting AOT
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# Native AOT Feasibility (2026-10-12 — landed 2026-10-12)

**English** | [简体中文](aot-feasibility.zh-CN.md)

**Verdict:** full-repo Native AOT is not feasible (EF Core / ASP.NET Core MVC upstreams do not support it). **The pure-managed leaf packages are marked `IsAotCompatible` and verified for real**: `tests/PubSoft.AotSmoke` (a `PublishAot=true` console referencing the leaf packages) publishes with `dotnet publish -r linux-x64` with **zero IL2xxx/IL3xxx warnings** and the native binary runs successfully. Entry point: `scripts/verify-aot-consumer.sh` (in CI; skips gracefully without a native toolchain).

## Hard Dependency Constraints (non-negotiable)

| Dependency | AOT status | Blast radius |
|---|---|---|
| EF Core 10 | **No Native AOT** (runtime model building is reflection-heavy; compiled models speed startup but don't change this) | `EntityFrameworkCore`, `Messaging.Outbox`, `Scheduling.EntityFrameworkCore`, all EF persistence paths |
| ASP.NET Core MVC | No (controller model binding/filters are reflection-driven); Minimal API partially | `AspNetCore` (contains MVC filters) |
| StackExchange.Redis | No AOT commitment; verify per version | `Caching.Redis`, `Security.Redis` |

## Package Tier Matrix

### Tier A: essentially clean today (no reflection hotspots)

`Core`, `Domain`, `Contracts`, `Caching` (the in-process lock is a pure concurrency primitive), `Security`, `Cryptography` (BouncyCastle is managed-only, low reflection risk), `Logging`, `Notifications`, `MultiTenancy`, `Scheduling`, `Events` (`MakeGenericType` is metadata-only and AOT-safe; `GetServices(Type)` uses MS.DI's annotated path), `Tokens` (manual `JsonElement` parsing, no reflective serialization), `Testing`, `Monitoring`, `Resilience` (standard `HttpClient`/`DelegatingHandler` in `.Http`), `Authentication` (snapshot + resolver, pure types), `Outbound` (see Tier B), `Identity` (pure domain model).

### Tier B: feasible with code adjustments

| Package | Issue | Resolution |
|---|---|---|
| `Mapping` | `Expression.Compile()` + runtime `GetProperties` | `Compile` falls back to the interpreter under AOT (.NET 8+, correct but slower); generic params need `[DynamicallyAccessedMembers(PublicProperties \| ParameterlessConstructors)]` against trimming |
| `Outbound` | `GetProperties`/`GetCustomAttribute<ApiFieldAttribute>` on arbitrary request/response types; `JsonSerializer.Deserialize(value, Type)`; `List<>` `MakeGenericType` + `Activator.CreateInstance` | Member reflection gets `DynamicallyAccessedMembers`; runtime-typed STJ either flows through a `JsonTypeInfoResolver` options or is marked `RequiresUnreferencedCode`; value-type `MakeGenericType` may lack instantiations under AOT — test or annotate |
| `Messaging` | `SerializeToElement(payload, options)` | Already takes host `JsonSerializerOptions` — documented: hosts supply a source-gen `JsonSerializerContext` |
| `Messaging.Outbox` | `JsonSerializer.Serialize(@event, eventType)` runtime-typed + EF dependency | EF already unsupported; if a non-EF contract package is ever split out, the serialization point must go through a resolver |
| `Auditing` | `JsonSerializer.SerializeToElement(value, type)` runtime-typed | Same as above |
| `Caching.Redis`/`Security.Redis` | `JsonSerializer.<T>` via host options (OK); StackExchange.Redis itself unverified | Serialization side is fine; the Redis client needs a standalone publish check |
| `AspNetCore` | Plain middleware can run under AOT hosts; `AuthorizeFilter`/MVC conventions depend on MVC | Either split middleware-only capabilities into a subset package, or mark the whole package non-AOT |
| `Authentication.Jwt.AspNetCore` | JwtBearer/`Microsoft.IdentityModel` recent versions are largely AOT-compatible | Needs end-to-end publish verification; the bearer itself has no MVC dependency |

### Tier C: unsupported (by dependency)

`EntityFrameworkCore`, `Scheduling.EntityFrameworkCore`, `Messaging.Outbox`, `Auditing.Outbox` (EF sink chain).

## Already AOT-Friendly Design (keep it)

- Every serialization point accepts a host `JsonSerializerOptions` — hosts inject a source-gen `JsonSerializerContext` and satisfy AOT; **never** add a "default options + runtime type" call site.
- No `Type.GetType` string resolution, no assembly-scanning registration — fail-closed dispatch/explicit registration is naturally AOT-compatible.
- All DI uses explicit `Add*` registrations; MS.DI is already annotated AOT/trim-compatible.

## Landed 2026-10-12

**Marked `IsAotCompatible`:** `Core`, `Domain`, `Contracts`, `Caching`, `Security`, `Cryptography`, `Logging`, `Notifications`, `MultiTenancy`, `Scheduling`, `Scheduling.Http`, `Events`, `Tokens`, `Testing`, `Monitoring`, `Monitoring.Scheduling`, `Resilience`, `Resilience.Http`, `Authentication`, `Authentication.Jwt`, `Authentication.OAuthProviders`, `Identity`, `Messaging`, `Authorization`, `Mapping`, `Outbound`, `Auditing`.

**Fix patterns applied:**
- Host-supplied `JsonSerializerOptions` STJ sites → `UnconditionalSuppressMessage` + contract documentation (`Messaging` envelope/dispatcher)
- Fixed library-type STJ → source-gen `JsonSerializerContext` (`Scheduling.Http`, `Monitoring.Scheduling`)
- Runtime-typed STJ / reflective contract mapping → `RequiresUnreferencedCode`/`RequiresDynamicCode` at public boundaries (`Outbound` engine chain, `Auditing` value serializer — consumers EF/AspNetCore are non-AOT anyway)
- Member reflection → `DynamicallyAccessedMembers` generic-parameter annotations (`Mapping`, `Outbound` metadata resolution, DI generic registration extensions)
- Name-based `Expression.Property` lookups → semantically equivalent reflection-free forms (`Mapping`'s `Nullable<T>` path)
- DI resolution of runtime event types → suppression + documented "delegate handler is the AOT path" (`Events`)

**Verification finding:** `dotnet publish` can skip the native link after package changes (incremental publish reuses a stale binary) — AOT smoke must clean publish outputs or use fresh obj/bin; never trust "the last binary".

**Pending upstream verification before marking:** `Caching.Redis`, `Security.Redis` (StackExchange.Redis has no commitment), `Authentication.Jwt.AspNetCore` (JwtBearer/IdentityModel).

## Implications for Host Ecosystems

ASP.NET Core MVC / EF Core / ABP hosts — **business services stay JIT forever** (no upstream AOT commitments). The value of AOT compatibility is PubSoft being consumed by other scenarios: CLIs, agents, edge functions. AOT investment should stay inside the low-cost leaf-package annotation band; no AOT compromises are made for the EF/ASP.NET adapter layers.
