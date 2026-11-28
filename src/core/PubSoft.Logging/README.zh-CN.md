---
purpose: PubSoft.Logging 包级接入指南
audience: 集成方宿主开发者
owner: PubSoft 维护组
last-reviewed: 2026-10-13
---

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

扩展注册 `AppLogger` 与 `AppLoggerProvider`，宿主使用已注册 logging provider 后参与常规 `ILogger<T>` 解析。logger 默认队列容量 8192、每批至多 128。自定义 sink 或队列容量可显式构造：

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

持续抛异常的 sink 会被隔离（quarantine），而不是每批都重试：连续失败达到 `LogSinkQuarantineOptions.FailureThreshold`（默认 3）后，按倍增退避暂停（`InitialBackoff` 1 秒，上限 `MaxBackoff` 30 秒）。暂停期满后下一批即为探针——成功则完全恢复，失败则以更长暂停重新隔离。隔离按 sink 独立生效，健康 sink 不受影响。`GetSinkStates()` 暴露诊断快照（每个 sink 的连续失败数、隔离截止时间、跳过/失败批次数），不含任何日志内容，可安全接入 health/metrics。被跳过的批次直接丢弃、不缓冲——隔离只是约束故障 sink 的成本，不构成可靠投递。

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

## 链路关联

`LogEntry` 携带 W3C 链路身份：日志调用发生在 ambient `System.Diagnostics.Activity` 内时（ASP.NET Core 入站请求、`ActivitySource` span、MQ 消费作用域），`AppLogger` 自动捕获 `trace_id`/`span_id` hex 字符串——与 `PubSoft.Tracing` span 文档及 `PubSoftAuditLogs.trace_id` 同格式。traces/logs/audit 三条观测腿可按此值 join，宿主零接线。显式传 `traceId` 参数永远优先于 ambient 捕获；此时不写本地 span id——本地 span 属于另一条 trace，配对会腐蚀关联。NDJSON 输出中 `trace_id` 一律为字符串而非数字，下游存储可按审计列同法索引。

## 滚动文件与保留

`RollingFileLogSink` 写 NDJSON 文件，命名 `{prefix}-{yyyyMMdd}.log`（UTC 日期）。UTC 日期变更即滚动；同一天内下一批写入将超过大小上限时也滚动，滚动段带 `_001`、`_002` 后缀。批次永不跨文件拆分；单批超过大小上限时整体写入。启动时 sink 采纳当天最新段（追加而非覆盖），重启不丢不重。

```csharp
var logger = new AppLogger(
    sinks:
    [
        new ConsoleLogSink(),
        new RollingFileLogSink(
            directory: "logs",
            filePrefix: "orders",
            maxFileSizeBytes: 32 * 1024 * 1024,   // 默认有界
            retainedFileCountLimit: 31,            // 保留最新的若干个文件
            retainedFileAgeLimit: TimeSpan.FromDays(14))
    ],
    minLevel: AppLogLevel.Info);
```

保留清理只在滚动边界执行而非每次写入：超数量上限的最旧文件、超年龄上限的文件删除。数量与年龄相互独立，传 `null` 可分别禁用。默认值刻意有界（最坏占用 ≈ 大小上限 × 数量上限，单前缀 GiB 量级）——无界的日志目录是生产事故；确需无界请显式传 `null`。写入按批追加、开-写-关语义——sink 继承 `AppLogger` 批处理的顺序单写者契约；多 logger 实例或多进程共享目录不做协调。

## 按源级别与动态开关

`AppLogger.MinLevel` 是 volatile 原子属性——运行时赋值即热调全局下限，无需重启。`AppLogger.LevelOverrides` 按最长前缀匹配为各源单独设级，ordinal 区分大小写：

```csharp
logger.LevelOverrides =
[
    new LogLevelOverride("Microsoft", AppLogLevel.Warn),              // 压框架噪音
    new LogLevelOverride("Microsoft.AspNetCore.Hosting", AppLogLevel.Info), // 单开子系统
    new LogLevelOverride("orders", AppLogLevel.Debug),                // 单开某域
];
```

低于有效级别的条目在入队前丢弃（零队列开销）。数组赋值即拷贝、原子换入，因此替换规则集**就是**动态开关——从配置监听、运维端点或开关回调里 mutate-and-replace：

```csharp
// 宿主侧示例：事故窗口内把某子系统开到 Debug，过后换回
logger.LevelOverrides = [.. logger.LevelOverrides, new LogLevelOverride("orders", AppLogLevel.Debug)];
```

MEL category 经桥接后即为日志源，覆盖规则对 `ILogger<T>` category 与直接用 `AppLogger` 的源同权生效。这与 `Microsoft.Extensions.Logging` 自身的 category 过滤相互独立（后者在更上游生效）。

## 生态定位

`ILogSink`（批量写契约）与 `LogContext`（环境富化）是扩展原语；厂商相关 sink——采集器、对象存储、代理商——由宿主自行实现。本包刻意不追逐 sink/enricher 生态广度：Serilog 这类社区级生态是十年第三方贡献的产物，不是可复制的"功能"。宿主若需要开箱即用的十余种厂商集成，应直接采用 Serilog 作为日志骨干——两者可共存，`PubSoft.Logging` 对其他包并非必备基础设施。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Logging`: `AppLogLevel` `AppLogger` `AppLoggerProvider` `AppVersion` `AsyncBackbone<T>` `ConsoleLogSink` `ILogSink` `JsonLogSink` `LogContext` `LogEntry` `LoggingServiceCollectionExtensions`

## 验证

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~LoggingTests
bash scripts/verify-package-consumer.sh
```
