---
purpose: package-level hosting guide for PubSoft.Resilience
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Resilience

**English** | [简体中文](https://github.com/pubsoft-cn/pubsoft/blob/main/src/core/PubSoft.Resilience/README.zh-CN.md)

Provider-neutral resilience primitives with no framework dependency — the policy layer that `PubSoft.Resilience.Http` and `PubSoft.Scheduling.Http` build on.

## Core Concepts

**`RetryPolicy`** — bounded retry with per-attempt and overall timeout budgets:

```csharp
var policy = new RetryPolicy
{
    MaxRetries = 2,                          // retries AFTER the initial attempt
    InitialDelay = TimeSpan.FromMilliseconds(200),
    JitterRatio = 0.2,
    RetryOn5xx = true,
    RetryOnConnectionFailure = true,
    RetryOnTimeout = true
};
```

- `GetDelay` returns the deterministic exponential base delay, capped at 30 s. Retry executors that use `GetDelayWithJitter` sample symmetrically inside the configured `JitterRatio` band (default ±20%) and keep the same 30 s cap; all delays consume the overall timeout
- `MaxRetries` counts retries, not total attempts (`MaxRetries = 2` → up to 3 executions)
- HTTP 408/429 response retries are explicit opt-in (`RetryOnRequestTimeout` / `RetryOnTooManyRequests`); other 4xx responses are never retried
- Caller cancellation propagates and is never treated as a retryable failure

**`CircuitBreaker`** — single-process failure-threshold circuit with half-open probing:

```csharp
var breaker = new CircuitBreaker
{
    Options = new CircuitBreakerOptions
    {
        FailureThreshold = 5,
        OpenTimeout = TimeSpan.FromSeconds(30)
    }
};
```

- Success closes/resets; failures, exhausted retries, and total-timeout failures count toward the threshold
- The default mode trips after a consecutive failure streak. `CircuitBreakerMode.RollingFailureRatio` is an explicit opt-in for noisy traffic: it trips only when at least `MinimumThroughput` outcomes inside `SamplingDuration` reach `FailureRatio`, reducing false positives from a small burst of 429/5xx responses mixed into otherwise healthy traffic
- An open breaker rejects immediately — no request is attempted
- A half-open probe is released on caller cancellation so a canceled request cannot strand the breaker
- State is in-process, keyed per breaker instance — not a distributed coordination mechanism

## Semantics and Boundaries

- Failure classification is the caller's job — the library does not guess which exceptions are transient
- No automatic application-level idempotency analysis: retry safety rules live at the adapter layer (`Resilience.Http` rejects unsafe methods entirely)
- All async APIs propagate `CancellationToken`

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Resilience`: `CircuitBreaker` `CircuitBreakerOptions` `CircuitState` `RetryExecution` `RetryPolicy` `RetryResult`

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**(options & records)**

`FailureRatio`, `FailureThreshold`, `InitialDelay`, `JitterRatio`, `MaxRetries`, `MinimumThroughput`, `OpenTimeout`, `Options`, `RetryOn5xx`, `RetryOnConnectionFailure`, `RetryOnRequestTimeout`, `RetryOnTimeout`, `RetryOnTooManyRequests`, `SamplingDuration`

<!-- pubsoft:api-surface:end -->

