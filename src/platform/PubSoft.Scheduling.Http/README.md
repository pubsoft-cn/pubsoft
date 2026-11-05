---
purpose: package-level hosting guide for PubSoft.Scheduling.Http
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Scheduling.Http

**English** | [简体中文](README.zh-CN.md)

HTTP executors for `PubSoft.Scheduling` jobs — no ASP.NET Core dependency.

## Registration

```csharp
services.AddPubSoftHttpExecutors();
// then configure jobs: ExecutorType "Http.Probe" or "Http.Resilient"
```

## Executors

| Executor | Behavior | Use for |
|---|---|---|
| `Http.Probe` | Single request; validates URL/headers/timeout + expected status; never retries | health/liveness checks |
| `Http.Resilient` | `PubSoft.Resilience` retry/circuit policy; async token acquisition for authenticated targets | calling internal HTTP endpoints |

`Http.Resilient` retries only bodyless `GET`/`HEAD`/`OPTIONS`/`TRACE` by default. Other methods or requests with content require the host to set `AllowUnsafeRetries = true` after confirming endpoint idempotency. Tokens, URLs, and credentials are never logged; error surfaces are sanitized.

## Egress control (SSRF)

Job URLs are constrained so a low-trust job writer cannot aim the scheduler at
internal or cloud-metadata endpoints:

- `HttpExecutorOptions.BlockPrivateNetworks` (default **true**) — refuses targets
  resolving to non-public addresses (RFC1918, loopback, link-local incl. cloud
  metadata `169.254.x.x`, CGNAT, multicast, reserved/transition ranges). The check
  runs **at connect time** via a `ConnectCallback` that resolves and pins the
  address itself, so DNS rebinding between validation and connect is closed.
- `HttpExecutorOptions.AllowedHosts` (default empty = no allowlist) — exact hosts
  (`api.example.com`) or suffix wildcards (`.example.com` / `*.example.com`).
  Configure it for strict egress isolation.
- `HttpExecutorOptions.FollowRedirects` (default **false**) — when enabled,
  redirects are followed manually (max 8): every hop is re-validated against the
  same scheme/allowlist/private-address rules, and the `TokenRef` credential
  header is stripped when a hop crosses to a different host. `POST` downgrades
  to `GET` on 301/302/303 (HttpClient parity).
- `HttpExecutorOptions.UseSystemProxy` (default **false**) — env/system proxies
  would bypass the connect-time check, so direct egress is the default; options
  validation **rejects** `UseSystemProxy` + `BlockPrivateNetworks` together.

Set `BlockPrivateNetworks = false` only when jobs legitimately probe inside the
host's own network (e.g. intranet health checks).

## Boundaries

Job registration, bitmask schedules, engine limits, and state stores live in [PubSoft.Scheduling](../../platform/PubSoft.Scheduling/README.md). This package adds executors only.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Scheduling.Http`: `HttpExecutorOptions` `HttpProbeExecutor` `HttpResilientExecutor` `ScheduledHttpRequest` `ScheduledHttpResult` `SchedulingHttpServiceCollectionExtensions`
Redirects are followed manually with per-hop target re-validation. When a redirect changes origin (scheme, host, or port), job-configured headers and the token header are no longer forwarded — credentials never cross origins.

