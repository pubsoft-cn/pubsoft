# PubSoft.Logging

[English](README.md) | **简体中文**

`PubSoft.Logging` 提供有界异步 logger、Console/NDJSON sink、不可变日志项/上下文快照与 `Microsoft.Extensions.Logging` provider。它是可选日志 provider，不替代应用专属的保留、脱敏或导出策略。

## 注册

注册内置 console sink，可选 JSON-lines sink：

```csharp
using Microsoft.Extensions.Logging;
using PubSoft.Logging;

services.AddPubSoftLogging(minLevel: AppLogLevel.Info, enableJsonSink: true);
```

扩展注册 `AppLogger` 与 `AppLoggerProvider`，宿主使用已注册 logging provider 后参与常规 `ILogger<T>` 解析。logger 默认 8,192 条队列、每批至多 128 条。自定义 sink 或队列容量可显式构造：

```csharp
var logger = new AppLogger(
    sinks: [new ConsoleLogSink(), new JsonLogSink(WriteToCollectorAsync)],
    minLevel: AppLogLevel.Warn,
    capacity: 4_096,
    batchSize: 64);
services.AddPubSoftLogging(logger);
```

`AppLogger` 为异步可释放。注册进宿主 DI 容器并让宿主关闭流程释放 provider；默认 dispose 时最多 30 秒内排空已接受条目。

## 队列与 Sink 语义

`AppLogger.Log` 及 `Trace`/`Debug`/`Info`/`Warn`/`Error`/`Fatal` 助手入队不等待。启用级别的条目无法入队时（队列满或排空中）返回 `false`；低于 `MinLevel` 的被过滤并返回 `true`。需要显式背压路径可用 `AsyncBackbone<LogEntry>.PublishAsync` 等待队列空位，但多数应用代码不应因慢 sink 阻塞请求路径。

sink 按批顺序接收。某 sink 失败不阻止后续 sink 收到该批；默认处理错误钩子不重新抛出。`PendingCount` 是瞬时队列计数，非投递确认。`DrainAsync(timeout)` 停止新写入并等待已接受工作；超时则取消 consumer，处理中的条目可能未到达全部 sink。除非 sink 提供更强保证，console/文件/采集器投递均按 best-effort 对待。

JSON sink 每行写一个 JSON 对象：时间戳、级别、source、消息、可选异常与结构化上下文字段。`enableJsonSink` 写标准输出。避免记录凭据、token、个人数据或未评审的异常文本——本包不自动脱敏。

## MEL Scope 与上下文

MEL provider 把 `ILogger` category 映射为日志 source、级别映射 `AppLogLevel`、`EventId` 进上下文字段。字典式 MEL scope 成为结构化 `LogContext` 属性。直接用 `AppLogger` 的应用代码应即时 push/release scope：

```csharp
using (LogContext.PushProperty("operation", "rebuild-index"))
{
    logger.Info("search.index", "Rebuild started");
}
```

上下文在条目创建时复制——后续 scope 变更不改已入队条目。上下文值保持小、有界、非敏感。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Logging`: `AppLogLevel` `AppLogger` `AppLoggerProvider` `AppVersion` `AsyncBackbone<T>` `ConsoleLogSink` `ILogSink` `JsonLogSink` `LogContext` `LogEntry` `LoggingServiceCollectionExtensions`

## 验证

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~LoggingTests
bash scripts/verify-package-consumer.sh
```
