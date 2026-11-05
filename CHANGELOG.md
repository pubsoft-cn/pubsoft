# Changelog

All notable changes to PubSoft packages. Format follows [Keep a Changelog](https://keepachangelog.com/);
versioning follows semver during the 0.x alpha line — breaking changes are called out explicitly.

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
