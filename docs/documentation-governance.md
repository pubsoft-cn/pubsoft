---
purpose: documentation tier structure, templates, and review discipline for PubSoft
audience: contributors and reviewers
owner: PubSoft maintainers
last-reviewed: 2026-11-05
---

# Documentation Governance

## Three-tier structure

| Tier | Location | Role |
|---|---|---|
| Overview | root `README.md` | project pitch + package registry table — every packable `PubSoft.*` must appear |
| Governance | `docs/` | templates, specs (this file), security-boundary analysis, doc index — the rules and cross-cutting documents |
| Package | `src/*/PubSoft.*/README.md` | package-level hosting guide — same section order as the template |

Bilingual pairs are mandatory on all three tiers (`README.md` + `README.zh-CN.md`).
Generated artifacts (coverage matrices, API exports) are exempt from the zh pair — they are regenerated, not authored.

## Templates

`docs/templates/package-readme.template.{md,zh-CN.md}` is the mandatory starting
point for new package docs. Required sections: Scope and Guarantees (trust
boundary + fail-closed defaults + opt-ins), Setup, Options, Residual Host
Responsibilities, Public API surface (generated note).

Every doc carries front-matter: `purpose / audience / owner / last-reviewed`.

## Review cadence

- `last-reviewed` drives a quarterly pass — stale docs are updated or deleted, never left to rot (living library).
- Doc ownership lives in the `docs/README.md` index table — every entry names an owner.

## Code examples must be trustworthy

README code blocks are claims, not decorations — they must match the shipped
public API. Direction (adopted from Microsoft Learn): a `tests/PubSoft.DocsSnippets`
project that compiles the exact snippets READMEs quote — planned next node; until
it lands, reviewers verify examples against `PublicAPI.Unshipped.txt` by hand.


## Change-trigger matrix

Each change class maps to mandatory artifacts — all of them, in the same commit node.
`check-commit.sh` verifies the detectable rows automatically.

| Trigger | Required artifacts (all in the same commit) |
|---|---|
| New package (`src/*/*.csproj` added) | root `README.md` registry row · package `README.md` + `README.zh-CN.md` from template · `PublicAPI.Unshipped.txt` · `PubSoft.sln` entry · `tests/` coverage |
| New public symbol | `PublicAPI.Unshipped.txt` entry (RS0016 enforced) · package README API-surface list if regenerated |
| Behavior/security-boundary change | package README Options/Guarantees sections · `docs/security-boundary-analysis` row if boundary-affecting · regression test |
| New doc in `docs/` | `.zh-CN.md` pair · front-matter · `docs/documentation-governance` index row |
| Package removed/renamed | root README row removal · sln cleanup · ledger cleanup · dependent README links |
| Version/release | `Directory.Build.props` `PubSoftVersion` · release note on breaking/behavior deltas |

## Doc index

| Document | Owner | Last reviewed |
|---|---|---|
| `docs/design-principles` | PubSoft maintainers | — |
| `docs/security-boundary-analysis` | PubSoft maintainers | — |
| `docs/packaging` | PubSoft maintainers | — |
| `docs/documentation-governance` | PubSoft maintainers | 2026-11-05 |
| `docs/templates/package-readme` | PubSoft maintainers | 2026-11-05 |
