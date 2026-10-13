# Design Principles

**English** | [简体中文](design-principles.zh-CN.md)

Every `PubSoft.*` package follows these rules. New packages and APIs do not deviate without a documented exception.

## Fail-Closed Defaults

Missing annotations, absent configuration, resolution failures, and unregistered verifiers **reject** rather than allow:

- Authorization: undeclared permissions, unknown modules, and unresolved subjects yield 401/403; there is no admin bypass.
- Scoping: a member scope with `IsMemberScoped` but no `MemberId` matches an empty result set instead of silently widening.
- Security: `[RequireClientCredential]` without a registered verifier returns 403; missing and invalid credentials return the same 401 (no enumeration oracle).
- Messaging: unknown `MessageType`s, duplicate aliases, and payload-type conflicts are rejected at registration/lookup time.
- Capacity: audit collectors, notification dedup, and in-process locks reject when full — nothing is silently dropped.

## Explicit Opt-In, No Implicit Behavior

- Middleware, filters, health endpoints, EF mappings, audit capture (IP/UA/actor), and migrations all require explicit host `Add`/`Use`/`Map` calls.
- Algorithms are caller-chosen: RS256 and HS256 signing are separate API families — no automatic selection, no cross-algorithm downgrade.
- No assembly scanning, no runtime type resolution (`Type.GetType`), no convention-based registration — discovery is either explicit registration or absent.

## Bounded Everything

Batches, queues, caches, collectors, rate limiters, and lock tables all carry capacity limits and fail closed when full. Every async API accepts and propagates a `CancellationToken`.

## Inversion of Control: Hosts Provide Data, the Library Provides Mechanism

The library never touches credential stores, permission stores, user databases, or the configuration system:

- `IOAuthChannelConfigProvider` / `IAlipayAppConfigProvider` — credential lookup is host-implemented
- `IPermissionGrantProvider` / `IClientCredentialVerifier` / `IEncryptionKeyRing` / `IAuditSnapshotSink` — same pattern
- `ITenantContext` / `IMemberScopeContext` — the library consumes the contracts; `Ambient*` implementations are optional host wiring

## Ambient Scope Rules (AsyncLocal)

`AmbientTenantContext`/`AmbientMemberScopeContext`'s `Change()` only affects **downstream code in the current async control flow**. A `Change` made inside an async callee never flows back to the caller frame — scope injection must therefore happen in a layer that wraps `await next()` (middleware, `IAsyncActionFilter`, `IEndpointFilter`), never in a filter without `next`.

## Concurrency Primitives in Two Layers

- **Lease** (time bound): Redis/in-process locks expire automatically — not a correctness guarantee.
- **Fencing** (monotonic token): protected resources must validate it — this is the correctness boundary.
- Optimistic concurrency: tenant/member scope columns double as EF concurrency tokens.
- No automatic lease renewal; no in-process lock masquerading as a distributed one.

## Honest Durability Semantics

- Outbox delivery is at-least-once; Inbox receipts only guarantee same-transaction database writes — external side effects need their own idempotency.
- Audit archival is export-before-delete; when the export targets the same DbContext the archive+delete commits atomically, otherwise it is at-least-once.
- Retention, archival, and cleanup are explicit host-invoked primitives — nothing schedules itself.

## AOT Policy

- **Leaf packages** (no EF Core / ASP.NET MVC / ABP dependencies): `IsAotCompatible`, IL warnings treated seriously, verified by the real `PublishAot` run in `tests/PubSoft.AotSmoke`.
- Host-supplied serialization types: accept `JsonSerializerOptions` (hosts inject a source-gen context) and mark `UnconditionalSuppressMessage` with justification.
- Fixed library types: source-generated `JsonSerializerContext`.
- Runtime type-resolution paths: marked `RequiresUnreferencedCode`/`RequiresDynamicCode` at the public boundary — honest, not hidden.
- EF/ASP.NET/ABP packages make no AOT compromises — upstream does not support it.

## Security Primitive Boundaries

- Keys and credentials never appear in logs or exception messages.
- The PBKDF2 hasher supports `VerifyAndRehash` for transparent upgrades; AES writes GCM only, legacy CBC is read-for-migration only.
- No built-in default keys, default credentials, or development backdoors.
- Timing-sensitive comparisons use `CryptographicOperations.FixedTimeEquals`.

## Package Layering

- `PubSoft.{Area}` — framework-neutral core contracts and implementations.
- `PubSoft.{Area}.{Tech}` — technology adapters (`.AspNetCore`, `.Redis`, `.EntityFrameworkCore`, `.Http`, `.Outbox`).
- `PubSoft.Abp.*` — optional ABP wiring only; never part of the generic core.
- Inter-package dependencies are governed by the `scripts/verify-package-dependencies.py` allowlist.
