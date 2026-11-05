---
purpose: package-level hosting guide for PubSoft.Scheduling
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Scheduling

**English** | [简体中文](README.zh-CN.md)


`PubSoft.Scheduling` is a Generic Host background scheduler with explicit executor and result-processor contracts. The core package has no ASP.NET, EF Core, or Redis dependency. It does not provide distributed job leases, cluster mutual exclusion, durable state by default, or misfire recovery.

## Registration

Register the scheduler, its job configuration, and singleton executor/processor implementations:

```csharp
using PubSoft.Scheduling;

services.AddPubSoftScheduler(options =>
{
    options.LoopInterval = TimeSpan.FromSeconds(1);
    options.ErrorDelay = TimeSpan.FromSeconds(5);
    options.MaxConcurrentJobs = 8;
    options.Jobs.Add(new ScheduledJob
    {
        Name = "daily-report",
        ExecutorType = "report",
        Schedule = ScheduleMasks.DailyAt(hour: 2, minute: 30),
        ResultProcessors = ["logging"]
    });
});
services.AddExecutor<ReportExecutor>();
services.AddResultProcessor<LoggingResultProcessor>();
```

`AddPubSoftScheduler` registers a hosted worker and `MemoryStateStore` unless the host has already registered another `IStateStore`. Job names must be unique case-insensitively, and every configured executor/processor name must resolve at runtime. Executors and processors are singletons; keep them thread-safe and use `IServiceScopeFactory` when scoped work such as a DbContext is required.

## Schedule Masks

`ScheduleMasks` provides interval, daily, and weekly helpers. `ScheduleMask` is a six-field calendar bitmask for seconds, minutes, hours, day-of-month, month, and day-of-week. All selected calendar fields must match. Matching uses the supplied `DateTimeOffset` fields and offset; choose the intended time zone explicitly when supplying the scheduler's `TimeProvider`/clock.

```csharp
var everyTenMinutes = ScheduleMasks.EveryMinutes(10);
var weekdaysAtNine = new ScheduleMask(
    seconds: 1UL,
    minutes: 1UL,
    hours: 1U << 9,
    daysOfMonth: 0xFFFFFFFE,
    months: 0x1FFE,
    daysOfWeek: (byte)((1 << (int)DayOfWeek.Monday)
        | (1 << (int)DayOfWeek.Tuesday)
        | (1 << (int)DayOfWeek.Wednesday)
        | (1 << (int)DayOfWeek.Thursday)
        | (1 << (int)DayOfWeek.Friday));
```

Schedules are polled against the current timestamp; a matching time that passes while the process is stopped is not replayed after restart. The engine does not implement cron parsing, misfire compensation, or daylight-saving policy beyond the supplied timestamp offset.

## Execution and State

The engine runs enabled jobs matching the current timestamp, bounded by `MaxConcurrentJobs`. Executor exceptions become a generic failed `JobResult`; configured result processors run sequentially afterward, and processor failures are logged without preventing later processors or the state update. Host cancellation propagates through the executor and processors.

Per-job execution controls:

- `PreventOverlap` (default `true`, in-process only) — a due tick is skipped while a previous run of the same job is still executing on this host. Multi-instance deployments still need an external lease; overlap skips are logged and do not overwrite the stored last result.
- `Timeout` — per-attempt timeout, cooperative: the attempt receives a cancellation that surfaces as a failed `JobResult` with status `408` when the executor honours it (host shutdown still cancels immediately; an executor ignoring the token is not force-killed).
- `Retry` — bounded exponential backoff (`MaxAttempts` / `InitialDelay` / `BackoffFactor` / `MaxDelay`). Retries apply to thrown exceptions, job timeouts, and failed results flagged `Retryable = true`. A failed result **without** `Retryable` is final: executors must opt in per failure so persistent errors are not masked behind retries.

```csharp
options.Jobs.Add(new ScheduledJob
{
    Name = "rollup", ExecutorType = "rollup",
    Schedule = ScheduleMasks.EveryMinutes(10),
    Timeout = TimeSpan.FromMinutes(5),
    Retry = new JobRetryPolicy { MaxAttempts = 3, InitialDelay = TimeSpan.FromSeconds(2) }
});
```

Retries add latency inside a single due tick; they do not replace idempotent job design, and a retried job still re-executes the whole `ExecuteAsync` body — keep attempt-level work idempotent.

The default `MemoryStateStore` is process-local and volatile. It keeps last results for seven days and supports custom state with optional expiry. It is suitable for development or a single-process deployment where losing state on restart is acceptable. For persistence, use the separate `PubSoft.Scheduling.EntityFrameworkCore` package and validate its provider behavior for the production database. Persistence alone does not add a distributed lease, atomic read-modify-write, cluster exclusion, or misfire recovery.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Scheduling`: `ExecutorRegistry` `IExecutorRegistry` `IJobExecutor` `IResultProcessor` `IResultProcessorRegistry` `IStateStore` `JobResult` `JobRetryPolicy` `LoggingResultProcessor` `MemoryStateStore` `ResultProcessorRegistry` `ScheduleMask` `ScheduleMasks` `ScheduledJob` `SchedulerEngine` `SchedulerHostedService` `SchedulerOptions` `SchedulerServiceCollectionExtensions`

## Verification

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~SchedulingTests
bash scripts/verify-package-consumer.sh
```