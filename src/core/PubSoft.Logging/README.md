---
purpose: package-level hosting guide for PubSoft.Logging
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Logging

**English** | [简体中文](https://github.com/pubsoft-cn/pubsoft/blob/main/src/core/PubSoft.Logging/README.zh-CN.md)


`PubSoft.Logging` provides a bounded asynchronous logger, console and NDJSON sinks, immutable log entries/context snapshots, and a `Microsoft.Extensions.Logging` provider. It is an alternative logging provider, not a replacement for application-specific retention, redaction, or export policy.

## Registration

Register the built-in console sink and optionally the JSON-lines sink:

```csharp
using Microsoft.Extensions.Logging;
using PubSoft.Logging;

services.AddPubSoftLogging(minLevel: AppLogLevel.Info, enableJsonSink: true);
```

The extension registers `AppLogger` and `AppLoggerProvider`; the provider participates in normal `ILogger<T>` resolution after the host uses the registered logging providers. The logger defaults to an 8,192-entry queue and batches up to 128 entries. For custom sinks or queue sizing, construct `AppLogger` explicitly:

```csharp
var logger = new AppLogger(
    sinks: [new ConsoleLogSink(), new JsonLogSink(WriteToCollectorAsync)],
    minLevel: AppLogLevel.Warn,
    capacity: 4_096,
    batchSize: 64);
services.AddPubSoftLogging(logger);
```

`AppLogger` is asynchronous-disposable. Register it with the host DI container and allow host shutdown to dispose the provider; disposal drains accepted entries for up to 30 seconds by default.

## Queue and Sink Semantics

`AppLogger.Log` and its `Trace`/`Debug`/`Info`/`Warn`/`Error`/`Fatal` helpers enqueue without waiting. They return `false` if an enabled entry cannot be accepted, such as when the bounded queue is full or draining; entries below `MinLevel` are filtered and return `true`. If callers need an explicit backpressure path, `AsyncBackbone<LogEntry>.PublishAsync` waits for queue capacity, but most application code should avoid blocking request paths on a slow sink.

Sinks receive batches sequentially. A sink failure does not prevent later sinks from receiving the batch; the default processing-error hook does not rethrow. `PendingCount` is an instantaneous queue count, not a delivery acknowledgment. `DrainAsync(timeout)` stops new writes and waits for accepted work. If the timeout expires, it cancels the consumer, so entries still being processed may not reach every sink. Treat console/file/collector delivery as best-effort unless the sink supplies stronger guarantees.

A sink that keeps throwing is quarantined instead of retried on every batch: after `LogSinkQuarantineOptions.FailureThreshold` consecutive failed batches (default 3) the sink is paused for a doubling backoff (`InitialBackoff` 1 s up to `MaxBackoff` 30 s). When the pause expires, the next batch acts as the probe — success resets the sink to fully healthy, failure re-quarantines it with a longer pause. Quarantine is per sink; healthy sinks keep receiving every batch. `GetSinkStates()` exposes the diagnostics snapshot (per-sink consecutive failures, quarantine deadline, skipped/failed batch counts) with no entry payloads, so surfacing it to health or metrics cannot leak log content. Skipped batches are dropped, not buffered — quarantine bounds the cost of a broken sink, it does not make delivery reliable.

The JSON sink writes one JSON object per line, including timestamp, level, source, message, optional exception and structured context fields. `enableJsonSink` writes to standard output. Avoid logging credentials, tokens, personal data, or unreviewed exception text; this package does not automatically redact fields.

## MEL Scopes and Context

The MEL provider maps `ILogger` categories to the log source, levels to `AppLogLevel`, and `EventId` to context fields. Dictionary-like MEL scopes become structured `LogContext` properties. For application code using `AppLogger` directly, push scoped properties and dispose scopes promptly:

```csharp
using (LogContext.PushProperty("operation", "rebuild-index"))
{
    logger.Info("search.index", "Rebuild started");
}
```

Context is copied when the entry is created, so later scope changes do not mutate queued entries. Keep context values small, bounded, and non-sensitive.

## Trace Correlation

`LogEntry` carries W3C trace identity: when a log call runs inside an ambient `System.Diagnostics.Activity` (ASP.NET Core incoming requests, `ActivitySource` spans, MQ consumer scopes), `AppLogger` automatically captures `trace_id`/`span_id` as hex strings - the same format `PubSoft.Tracing` span documents and `PubSoftAuditLogs.trace_id` use. The three observability legs (traces, logs, audit) are joinable on this value without any host wiring. An explicit `traceId` argument always wins over the ambient capture; in that case no local span id is written, because the local span belongs to a different trace. NDJSON output writes `trace_id` as a string - never as a number - so downstream stores index it the same way as the audit column.

## Rolling Files and Retention

`RollingFileLogSink` writes NDJSON files named `{prefix}-{yyyyMMdd}.log` (UTC date). The active segment rolls when the UTC date changes, and within a day when the next batch would exceed the size limit — rolled segments take `_001`, `_002`, ... suffixes. A batch is never split across files; a single batch larger than the size limit is written whole. On startup the sink adopts the newest same-day segment (appending, never overwriting), so restarts do not lose or duplicate content.

```csharp
var logger = new AppLogger(
    sinks:
    [
        new ConsoleLogSink(),
        new RollingFileLogSink(
            directory: "logs",
            filePrefix: "orders",
            maxFileSizeBytes: 32 * 1024 * 1024,   // bounded by default
            retainedFileCountLimit: 31,            // keep only the newest files
            retainedFileAgeLimit: TimeSpan.FromDays(14))
    ],
    minLevel: AppLogLevel.Info);
```

Retention runs on roll boundaries only, not on every write: files beyond the count limit (oldest first) or older than the age limit are deleted. The count and age limits are independent and either can be disabled with `null`. Defaults are deliberately bounded (worst case ≈ size limit × count limit, on the order of a gibibyte per prefix) because an unbounded log directory is a production incident; pass `null` explicitly if unbounded growth is intended. Writes append per batch with open-write-close semantics — the sink inherits the sequential single-writer contract of `AppLogger` batching; sharing one directory across multiple logger instances or processes is not coordinated.

## Per-Source Levels and Dynamic Switching

`AppLogger.MinLevel` is a volatile atomic property — assigning it tunes the global minimum at runtime without restart. `AppLogger.LevelOverrides` adds longest-prefix-match rules for individual sources, ordinal case-sensitive:

```csharp
logger.LevelOverrides =
[
    new LogLevelOverride("Microsoft", AppLogLevel.Warn),              // quiet the framework noise
    new LogLevelOverride("Microsoft.AspNetCore.Hosting", AppLogLevel.Info), // except one subsystem
    new LogLevelOverride("orders", AppLogLevel.Debug),                // un-mute one domain
];
```

Entries below the effective level are dropped before enqueueing (zero queue cost). The array is copied on assignment and swapped atomically, so replacing it **is** the dynamic switch — mutate-and-replace from a config watcher, operator endpoint, or feature-flag callback:

```csharp
// host-side example: flip one subsystem to Debug for an incident window, then swap back
logger.LevelOverrides = [.. logger.LevelOverrides, new LogLevelOverride("orders", AppLogLevel.Debug)];
```

MEL categories flow through the bridge as log sources, so overrides apply uniformly to `ILogger<T>` categories and direct `AppLogger` sources. This is independent of `Microsoft.Extensions.Logging`'s own category filtering, which runs upstream when configured.

## Ecosystem Positioning

`ILogSink` (batch write contract) and `LogContext` (ambient enrichment) are the extensibility primitives; hosts implement vendor-specific sinks — collectors, object storage, vendor agents — in their own code. This package deliberately does not chase sink/enricher ecosystem breadth: community-scale ecosystems like Serilog's are the product of a decade of third-party contributions and are not a replicable feature. If the host needs a dozen vendor integrations out of the box, adopt Serilog as the logging backbone instead — the two can coexist, `PubSoft.Logging` is not mandatory infrastructure for the other packages.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Logging`: `AppLogLevel` `AppLogger` `AppLoggerProvider` `AppVersion` `AsyncBackbone<T>` `ConsoleLogSink` `ILogSink` `JsonLogSink` `LogContext` `LogEntry` `LoggingServiceCollectionExtensions`

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**Registration**

| Method |
|---|---|
| `AddPubSoftLogging` |

**(options & records)**

`ConsecutiveFailures`, `FailedBatches`, `FailureThreshold`, `InitialBackoff`, `IsQuarantined`, `LevelOverrides`, `MaxBackoff`, `MinLevel`, `QuarantinedUntil`, `SinkName`, `SkippedBatches`

<!-- pubsoft:api-surface:end -->

## Verification

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~LoggingTests
bash scripts/verify-package-consumer.sh
```

