---
purpose: package-level hosting guide for PubSoft.Logging
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Logging

**English** | [简体中文](README.zh-CN.md)


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

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Logging`: `AppLogLevel` `AppLogger` `AppLoggerProvider` `AppVersion` `AsyncBackbone<T>` `ConsoleLogSink` `ILogSink` `JsonLogSink` `LogContext` `LogEntry` `LoggingServiceCollectionExtensions`

## Verification

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~LoggingTests
bash scripts/verify-package-consumer.sh
```