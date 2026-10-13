# Packaging & Release Engineering

**English** | [简体中文](packaging.zh-CN.md)

## Versioning & License

- `Directory.Build.props` owns `PubSoftVersion` (currently `0.1.0`); individual csproj files do not set `<Version>`.
- License: MIT (root `LICENSE` + `PackageLicenseExpression`); security contact: pubsoft@gmail.com (`SECURITY.md`).
- Each `src/*/PubSoft.*/README.md` ships inside its nupkg (`PackageReadmeFile`); keep the module-level hosting guide next to the code.

## API Baseline

- All projects run `Microsoft.CodeAnalysis.PublicApiAnalyzers` (`src/Directory.Build.props`).
- Per package: `PublicAPI.Shipped.txt` (released surface) + `PublicAPI.Unshipped.txt` (unreleased additions). New public symbols must be seeded or RS0016 warns.
- `bash scripts/sync-public-api.sh` rebuilds and rewrites every `Unshipped.txt` from the analyzer output (handles RS0016 adds **and** RS0017 removals); `--check` verifies drift without writing — wire it into CI when the API surface should stay declared.
- `RS0026` (overloads with optional parameters) is a deliberate API style and is suppressed project-wide.
- On release, fold `Unshipped` into `Shipped` to establish the compatibility baseline.

## Dependency Governance

- Third-party dependency allowlist: `scripts/verify-package-dependencies.py` parses every nupkg's nuspec and compares it against the allowlist; new dependencies require approval first.
- Every package has an isolated consumer (`tests/consumers/PubSoft.{X}.Only`) that references only that package plus host-declared dependencies, and is verified end-to-end by `verify-package-consumers.py`.
- Local delivery: `dotnet pack` to a local feed. No floating versions (`latest`/`*`); prefer dependency versions published at least 7 days ago.

## AOT Verification

- Leaf packages follow the AOT policy in [design-principles.md](design-principles.md); `scripts/verify-aot-consumer.sh` performs a real `PublishAot` publish + run (skips gracefully without a native toolchain; wired into CI).
- ⚠️ Incremental `PublishAot` may reuse a stale native link — repackaging requires a clean republish to actually verify.

## CI (`.github/workflows/ci.yml`)

restore → Release build (warnings treated strictly) → full test suite → dependency allowlist → isolated consumers → AOT smoke → gitleaks secret scan (`fetch-depth: 0`). CI contains no publish step — releases require a separately authorized pipeline.

## Environment-Gated Tests

`[RedisFact]`/`[MySql84Fact]` tests skip automatically when `PUBSOFT_REDIS`/`PUBSOFT_MYSQL` are unset. Current baseline: 443 passed / 19 environment-skipped. Coverage guarantees apply only to the tested Redis/MySQL scenarios.

## Release Stages

- **Internal validation**: `$(PubSoftVersion)` carries a `-alpha.N` suffix. Pack to a feed directory consumed via a `local-dev` NuGet source; prerelease packages never appear as stable, so business validation never claims a version number the public release needs.
- **Public release**: drop the prerelease suffix, bump the stable version, and publish to NuGet.org after the repository goes public. `RepositoryUrl` is already embedded in package metadata.
