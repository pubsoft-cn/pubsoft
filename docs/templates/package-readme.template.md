---
purpose: <one line — what this package is for>
audience: <integrating host developers | operators | reviewers>
owner: <name or team>
last-reviewed: <YYYY-MM-DD>
---

# PubSoft.<PackageName>

One-line purpose — what this package provides and the boundary it guarantees.

> Doc pair required: this file (en) + `README.zh-CN.md` (zh-CN, same section order).
> Register the package in the root `README.md` feature table when adding it.
> Front-matter is mandatory metadata — `purpose/audience/owner/last-reviewed` feeds the quarterly doc review.

## Scope and Guarantees

What is enforced vs. delegated to the host — name the trust boundary explicitly.
State fail-closed defaults and any explicit opt-in flags (e.g. `AllowInsecure*`).

## Setup

```csharp
// minimal registration example — services.AddPubSoft<Feature>(...)
```

## Options

| Option | Default | Meaning |
|---|---|---|
| `Xxx` | `...` | ... |

## Residual Host Responsibilities

What this package deliberately does NOT do — authz on endpoints, secret custody,
egress policy, retention, etc. Never imply coverage the package does not provide.

## Public API surface

Generated from `PublicAPI.Unshipped.txt` — regenerate via `dotnet format analyzers`.
