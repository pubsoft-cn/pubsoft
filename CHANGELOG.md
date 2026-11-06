# Changelog

All notable changes to PubSoft packages. Format follows [Keep a Changelog](https://keepachangelog.com/);
versioning follows semver during the 0.x alpha line — breaking changes are called out explicitly.

## [0.1.0-alpha.14] — 2026-11-14

### Breaking
- **Persisted IDs are `long` Snowflake, not `Guid`.** `AuditLogRecord.Id`,
  `AuditEntityChangeRecord.Id`/`AuditLogId`, `OutboxMessage.Id`,
  `InboxReceipt.MessageId`, `IOutboxPublisher.PublishAsync` (now `Task<long>`),
  and every `IOutboxStore`/`IInboxMessageProcessor` signature moved from
  `Guid` to `long`. `LeaseToken` remains `Guid` — it is a fencing nonce, not
  an identifier.
- **EF audit persistence and the outbox now require an `IIdGenerator`**
  registration (`services.AddPubSoftIdGenerator(workerId)`). Both resolve it
  at DI build and fail closed when absent — no silent `Guid.NewGuid()`
  fallback exists anywhere on the persisted-id path.
- `EfCoreOutboxPublisher<T>` / `EfCoreAuditSnapshotSink<T>` constructors take
  `IIdGenerator` as a required dependency.
- `SchedulerEngine` constructor gained an `IDistributedLock?` parameter for
  multi-replica mutual exclusion.

### Added
- `IIdGenerator` (`PubSoft.Core`) — client-side `long` id strategy seam;
  `SnowflakeIdGenerator` is the built-in 53-bit JS-safe implementation.
- `AddPubSoftIdGenerator` (`PubSoft.DependencyInjection`) — three
  registrations: explicit `workerId`, options delegate, and
  `PubSoft:IdGenerator:WorkerId` config binding. Missing/invalid worker id
  throws at registration; `TryAdd` yields to a host-provided generator.
- `LegacyIdMap` + `ConfigurePubSoftLegacyIdMaps()` — Guid→long migration
  shim: unique `(EntityType, LegacyId)` → `NewId` mapping, retireable after
  cutover.
- `ConfigEncryptionKeyRing` + `EncryptionKeyRingOptions` — built-in
  config-driven `IEncryptionKeyRing` (`ActiveKeyId` + `Keys:{id}=base64`);
  KMS-backed implementations still plug in through the contract.
- `IClientSignatureVerifier` + `ClientSignatureRequest`
  (`PubSoft.AspNetCore`) — signed client-credential seam: when the
  `X-Signature` header is present the host-registered verifier owns the
  signing protocol end to end (request material + body stream, not a hash);
  unsigned requests keep the plaintext `IClientCredentialVerifier` path.
- `AddCurrentSubjectAuditActor` — bridges `ICurrentSubjectAccessor` into
  `AuditActorContext` so resolved subjects flow into audit records with no
  mapping code.
- `SchedulerOptions.DistributedLocking` + `DistributedLockLease` — every due
  job acquires `pubsoft:sched:{jobName}` via the host `IDistributedLock`;
  enabled-but-unregistered skips all jobs (fail-closed).
- `RedisHealthCheck` + `AddPubSoftRedisCheck` — PING + latency probe for
  `/health` endpoints, mirroring `EfCoreHealthCheck`.
- `IdentityUserLogin` + `ConfigurePubSoftIdentityUserLogins()` — external
  login bindings (`LoginProvider`/`ProviderKey` → user) with a unique
  `(TenantId, LoginProvider, ProviderKey)` index and `ExtJson` extras.
- `HmacSignatureEncoding`/`HmacTimestampUnit` — `HmacValidationOptions`
  gains encoding (LowerHex/Base64) and timestamp-unit (Seconds/Milliseconds)
  profiles so both the `ClientSigner` wire format and hex/second peers
  validate.
- Per-cid/per-client JWT signing keys, configurable JWT claim dialect,
  identity lockout + `ExtJson`, EF encrypted-string conversion,
  `IEncryptionKeyRing` seam, audit `Detail` mapping.

### Changed
- All persisted `long` PKs enforce `ValueGenerated.Never`
  (`ApplySnowflakeIdConventions`) — EF never back-fills an identity/NEWID.
- `IdentityUser` lockout fields and `ExtJson` extension column added to the
  identity schema surface.
- Machine-token issuers validate per-cid signing keys when a
  `IClientSigningKeyProvider` resolves one.

### Docs / tooling
- `PUBSOFT037` — source `///` docs must be ASCII/English; zh-CN content
  ships through the `lib/{tfm}/zh-CN/` satellite IntelliSense pipeline
  (VS picks the file matching its UI language).
- `PUBSOFT038` — every public/protected member requires `///` docs; gated
  per package via `PubSoftDocDiscipline=full`. All 42 packages swept to
  zero violations (~1.5k members documented).
- `docs/host-integration-checklist.md` gains an *Identity generation*
  section; 11 package READMEs synced in both languages; docs index now
  lists governance + checklist entries.

## [0.1.0-alpha.11] — 2026-11-05

### Changed
- `EfCoreHealthCheck<TDbContext>`: base/abstract context types are now rejected at
  registration (`ArgumentException`) instead of failing health checks forever —
  pass the concrete `AddDbContext`-registered type. `IDbContextFactory<T>`-based
  contexts are supported.
- `IPubSoftModule`: `OnInitializationAsync`/`OnShutdownAsync` added (sync hooks
  bridge for compatibility); `ModuleOrchestrator` is async-first.
- `AddPubSoftMachineTokens`: `ITokenProvider` also registered as keyed service
  `"machine"` — resolve via `[FromKeyedServices(MachineTokenProvider.ServiceKey)]`
  when multiple providers coexist.

### Added
- `WebModuleContext` + `UsePubSoftModulesAsync` — web-host module lifecycle glue;
  `StartPubSoftModulesAsync` covers generic hosts.
- `MachineTokenOptions.IsValid` — issuer/audience/key-id hygiene, lifetime cap,
  256–1024-bit signing-key validation at startup.

### Docs / tooling
- Three-tier documentation governance + templates + change-trigger matrix.
- `LLMS.txt` AI-integration index; pre-commit gate scripts
  (`check-commit.sh`, `check-doc-drift.sh`, `verify-before-push.sh`).
