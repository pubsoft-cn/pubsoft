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

## Boundaries

Job registration, bitmask schedules, engine limits, and state stores live in [PubSoft.Scheduling](../../platform/PubSoft.Scheduling/README.md). This package adds executors only.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Scheduling.Http`: `HttpExecutorOptions` `HttpProbeExecutor` `HttpResilientExecutor` `ScheduledHttpRequest` `ScheduledHttpResult` `SchedulingHttpServiceCollectionExtensions`
