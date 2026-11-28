# Changelog

All notable changes to PubSoft packages. Format follows [Keep a Changelog](https://keepachangelog.com/);
versioning follows semver during the 0.x alpha line — breaking changes are called out explicitly.

## [0.1.0-alpha.24] — 2026-11-28

### Breaking
- **Outbox event contract keys are mandatory.** `IOutboxPublisher.PublishAsync` requires the explicit `eventType` argument — the CLR full-name fallback is removed. Audit snapshot events use the stable key `pubsoft.auditing.snapshot.v1`; consumers matching CLR type names must migrate or drain the outbox first.
- **MQTT inbox acknowledgement is manual.** `MqttInboxSubscriber` acknowledges only after inbox admission plus handler completion; handler failures now redeliver instead of being silently dropped.
- **Logging sink failure behavior.** Repeatedly throwing sinks are quarantined with doubling backoff instead of retried every batch; skipped batches drop. Monitor via `GetSinkStates()`.

### Added
- `IOutboxEventSerializer` seam for explicit JSON policy and source-generated metadata on trimmed/AOT hosts.
- `EfCoreTransactionExecutor` — fail-closed single-DbContext transaction primitive with explicit commit-verification probes.
- `OutboxWorkerStatusTracker` — low-cardinality worker status snapshot (poll lifecycle, consecutive failures, batch outcomes) for host health wiring.
- Edge client source-generated envelope context + `JsonTypeInfo` response seam; `EdgeAuthScheme` wire names pinned byte-identical across paths.
- `LogSinkQuarantineOptions`/`LogSinkState` diagnostics.
- Rolling failure-ratio circuit-breaker mode (opt-in), bounded retry jitter, bounded `Retry-After` honoring within the total timeout budget.
- `BoundedJitter` + `ReconnectJitterRatio` (default 0.2) on AMQP/MQTT inbox reconnects.
- Analyzer wave 3: PUBSOFT059-062 (`GC.Collect`, vacuous rows-affected checks, ignored `TryParse`, discarded fire-and-forget tasks).

### Security
- Redis refresh-token family consumption fixed for Redis Cluster: the family key is passed explicitly to the atomic consume script; missing token records can no longer be recreated by a consume race.

## [0.1.0-alpha.23] — 2026-11-26

### Fixed
- Configuration-binding and environment-alert resend gaps closed from business feedback.
- Durability and correctness gaps closed from the full-surface defect review.
- RabbitMQ CI node boots as the rabbitmq user so it survives prelaunch.

### Changed
- CI: tiered gates, shared coverage gate, AMQP integration coverage; pre-push gate mirrors the CI integration-test environment.

## [0.1.0-alpha.22] — 2026-11-25

### Added
- Notification and alerting robustness wave; alert rule recipients and template code pass-through.
- AMQP inbox subscriber completing the AMQP consume path.
- Readable scheduler configuration binding with startup lint.

### Fixed
- HTTP probe health checks run in parallel.
- Config parse and HTTP failure diagnostics surfaced.
- String-enum alert rule deserialization in `AlertJsonContext`.

## [0.1.0-alpha.21] — 2026-11-20

### Added
- W3C correlation propagation and `PubSoft.Tracing` span exporter.
- Audit rows stamped with the ambient W3C trace id.
- Native AOT consumer smoke covers `PubSoft.Tracing`.

### Fixed
- Hot-path allocations trimmed in signing, logging and JWKS paths.
- Audit hardening rounds: lifecycle, bounds, fencing monotonicity, strict JWKS bounds, caller-owned key lifetime, indexable keyword.

### Security
- Transitive `Logging.Abstractions` pinned via direct references.

## [0.1.0-alpha.20] — 2026-11-18

### Added
- Fenced executor and periodic worker over distributed locks.
- Configuration-driven cache provider selection.
- AMQP outbox dispatcher with publisher-confirm delivery.
- EF paging extension and wired slow-query interceptor.
- Edge sign-key pool status surface for readiness.
- Startup validation checks with detailed options errors and non-empty config read.

### Fixed
- Machine token signing-key validation measures UTF-8 byte length.

## [0.1.0-alpha.19] — 2026-11-14

### Changed
- Dependency servicing wave: Microsoft 10.0.15 + StackExchange.Redis 3.4.35.

### Fixed
- Allow-list README examples use documentation prefixes instead of private networks.

## [0.1.0-alpha.18] — 2026-11-14

### Added
- Allow-list gate primitives with explicit empty policy.
- Configure-request hook on token credentials; caller-auth configure hook on the remote client credential verifier.
- JWKS client-assertion key provider.
- Remote edge sign-key provider with registry refresh and local fallback.

### Changed
- Analyzer wave 2 (PUBSOFT056-058) plus IDE0005/CA53xx quality gates.

### Fixed
- HTTP factory-client disposal, exported-types guard, ordinal string compare.

## [0.1.0-alpha.17] — 2026-11-11

### Added
- Portable EF security stores with isolated consumer smoke.
- Refresh family rotation and token denylist with memory/Redis/EF stores.
- Client credential upgrades: remote verifier and RS256 assertions.
- JWT issuer signing keyring with hosted JWKS endpoint.

### Security
- Audit wave: fail-closed scoped delete and audit query, migrator compare-and-swap, diagnostics isolation.

### Fixed
- Migrator keyset pagination; audit capture-flags startup warning.

## [0.1.0-alpha.16] — 2026-11-10

### Breaking
- `AuditMiddlewareOptions` capture flags (`CaptureClientIp`/`CaptureUserAgent`/`CaptureActor`) default off — see upgrade notes for the silent forensic-data-loss warning.
- `AesGcmEncryptor.Decrypt` throws `PubSoftDecryptionException` (a `CryptographicException` subclass); exact-type catches must migrate.

### Added
- Metadata-bound encryption column migrator.
- Typed decryption failures, key rotation, config registration and startup probe.
- Startup diagnostics pipeline with upgrade notes.
- Dual version stream (`alpha.*` releases / `dev.*` internal iterations).

### Changed
- Review-hardening analyzers PUBSOFT045-054 + DocsTool unified ledger CLI; doc governance sweeps.

### Fixed
- `Use*` middleware null-options startup crash (PUBSOFT055).
- External review round: key validation, OAuth appid, rate-limit races.

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
