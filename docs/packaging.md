---
purpose: packaging, versioning, and release engineering
audience: contributors and release maintainers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# Packaging & Release Engineering

**English** | [简体中文](packaging.zh-CN.md)

## Versioning & License

- `Directory.Build.props` owns `PubSoftVersion` (currently `0.1.0-alpha.10`); individual csproj files do not set `<Version>`.
- License: MIT (root `LICENSE` + `PackageLicenseExpression`); security contact: pubsoft@gmail.com (`SECURITY.md`).
- Each `src/*/PubSoft.*/README.md` ships inside its nupkg (`PackageReadmeFile`); keep the module-level hosting guide next to the code.

## API Baseline

- All projects run `Microsoft.CodeAnalysis.PublicApiAnalyzers` (`src/Directory.Build.props`).
- Per package: `PublicAPI.Shipped.txt` (released surface) + `PublicAPI.Unshipped.txt` (unreleased additions). New public symbols must be seeded or RS0016 warns.
- `dotnet run --project tools/PubSoft.DocsTool -c Release -- --sync` rebuilds and rewrites every `Unshipped.txt` from the analyzer output (handles RS0016 adds **and** RS0017 removals); `--check` verifies drift without writing — wire it into CI when the API surface should stay declared.
- `RS0026` (overloads with optional parameters) is a deliberate API style and is suppressed project-wide.
- On release, fold `Unshipped` into `Shipped` to establish the compatibility baseline.

## Dependency Governance

- Package-dependency consistency: `src/Directory.Build.targets` parses each nupkg's nuspec during pack and verifies declared dependencies match the project/package references; `scripts/sync-package-deps.py` regenerates nuspec metadata. It is a consistency check, not an approval allowlist.
- Every package has an isolated consumer (`tests/PubSoft.{X}ConsumerSmoke`, outside the solution) that references only that package plus host-declared dependencies, verified end-to-end by `scripts/verify-package-consumer.sh` against a freshly packed local feed.
- Local delivery: `dotnet pack` to a local feed. No floating versions (`latest`/`*`); prefer dependency versions published at least 7 days ago.

## AOT Verification

- Leaf packages follow the AOT policy in [design-principles.md](design-principles.md); `scripts/verify-aot-consumer.sh` performs a real `PublishAot` publish + run (skips gracefully without a native toolchain; wired into CI).
- ⚠️ Incremental `PublishAot` may reuse a stale native link — repackaging requires a clean republish to actually verify.

## CI (`.github/workflows/ci.yml`)

restore → Release build → full test suite (Redis + MySQL integration when services are up) → format check → pack → isolated consumers → AOT smoke → gitleaks secret scan (`fetch-depth: 0`) → vulnerability listing. High/critical NuGet advisories fail at restore via `NU1903`/`NU1904` warnings-as-errors. CI contains no publish step — releases require a separately authorized pipeline.

## Environment-Gated Tests

`[RedisFact]`/`[MySql84Fact]` tests skip automatically when `PUBSOFT_REDIS`/`PUBSOFT_MYSQL` are unset. Coverage guarantees apply only to the tested Redis/MySQL scenarios.

## Release Stages

- **Internal validation**: `$(PubSoftVersion)` carries a `-alpha.N` suffix. Pack to a feed directory consumed via a `local-dev` NuGet source; prerelease packages never appear as stable, so business validation never claims a version number the public release needs.
- **Public release**: drop the prerelease suffix, bump the stable version, and publish to NuGet.org after the repository goes public. `RepositoryUrl` is already embedded in package metadata.
