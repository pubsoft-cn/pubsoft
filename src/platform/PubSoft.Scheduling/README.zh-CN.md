---
purpose: PubSoft.Scheduling 包级接入指南
audience: 集成方宿主开发者
owner: PubSoft 维护组
last-reviewed: 2026-10-13
---

# PubSoft.Scheduling

[English](README.md) | **简体中文**

`PubSoft.Scheduling` 是 Generic Host 后台调度器，带显式执行器与结果处理器契约。核心包无 ASP.NET/EF Core/Redis 依赖。不提供分布式 job lease、集群互斥、默认持久化状态或 misfire 恢复。

## 注册

注册调度器、job 配置与单例执行器/处理器：

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

`AddPubSoftScheduler` 注册 hosted worker 与 `MemoryStateStore`（宿主已注册其他 `IStateStore` 时除外）。多副本部署设 `SchedulerOptions.DistributedLocking = true`——每个到期 job 经宿主 `IDistributedLock`（Redis 实现在 `PubSoft.Caching.Redis`）抢 `pubsoft:sched:{jobName}` 锁；开启但无锁注册时全部跳过（fail-closed 而非静默并发）。`DistributedLockLease`（默认 5min）应超过最长 job 运行时长。job 名大小写不敏感唯一；每个配置的 executor/processor 名运行时须可解析。executor 与 processor 为单例——保持线程安全，需要 DbContext 等 scoped 工作时用 `IServiceScopeFactory`。

## 排程位掩码

`ScheduleMasks` 提供 interval/daily/weekly 助手。`ScheduleMask` 是六字段日历位掩码：秒、分、时、月内日、月、星期。所有选中字段都须匹配。匹配使用所供 `DateTimeOffset` 的字段与偏移——提供 scheduler 的 `TimeProvider`/时钟时显式选择预期时区。

```csharp
var everyTenMinutes = ScheduleMasks.EveryMinutes(10);
var weekdaysAtNine = new ScheduleMask(
    seconds: 1UL, minutes: 1UL, hours: 1U << 9,
    daysOfMonth: 0xFFFFFFFE, months: 0x1FFE,
    daysOfWeek: (byte)((1 << (int)DayOfWeek.Monday) | (1 << (int)DayOfWeek.Tuesday)
        | (1 << (int)DayOfWeek.Wednesday) | (1 << (int)DayOfWeek.Thursday)
        | (1 << (int)DayOfWeek.Friday));
```

按当前时间戳轮询匹配；进程停止期间错过的匹配时间不会在重启后补跑。引擎不实现 cron 解析、misfire 补偿或超出所供时间戳偏移的夏令时策略。

从配置绑定 job（`AddPubSoftScheduler(IConfiguration)`）时，`Schedule` 接受一个 interval 快捷键或六个位掩码字段——二者不可混用：

```json
"Schedule": { "EveryMinutes": 5 }
"Schedule": { "Seconds": 1, "Minutes": 1, "Hours": 16777215,
              "DaysOfMonth": 4294967294, "Months": 8190, "DaysOfWeek": 127 }
```

`EverySeconds` / `EveryMinutes` / `EveryHours` 映射到 `ScheduleMasks` 助手，是推荐写法——`"Minutes": 1` 这类裸掩码选中的是**第 0 分**，不是"每分钟"，肉眼无法 review。未知键、标量 cron 字符串、`Cron` 键、快捷/位掩码混用一律在绑定时显式报错。启动时 hosted service 会打印每个 job 解码后的 schedule（`ScheduleMask.Describe()`，形如 `seconds={0} minutes=* hours=* ...`），并在"受限窗口内每秒触发"这种经典误配上告警。

`ExecutorConfig`/`ResultProcessorConfig` 负载中的标量值仅当转换能无损往返时才重定型：`"401"` 会变成 JSON 数字供 `ExpectStatusCodes` 这类类型化成员使用，而 `"007"`、`"+123"` 保持字符串。字符串成员（header 值、模板参数）接受这些重定型 token 并还原原文——配置的 `"8000"` 最终到达的仍是字符串 `"8000"`。消费方反序列化这些 payload 必须使用包级 `JsonSerializerContext`——它们自带 `ScalarStringJsonConverter`；未注册该 converter 的自定义 `JsonSerializerOptions` 会重新引入数字 token 失败。

## 执行与状态

引擎运行匹配当前时间戳的启用 job，受 `MaxConcurrentJobs` 限制。executor 异常成为通用失败 `JobResult`；配置的结果处理器随后顺序执行，处理器失败记日志但不阻止后续处理器或状态更新。宿主取消传播至 executor 与 processor。

job 级执行控制：

- `PreventOverlap`（默认 `true`，仅进程内）——同名 job 上一轮仍在**本实例**执行时，本次到点跳过；多副本部署仍需外部 lease；跳过仅记日志，不覆盖已存的 last result
- `Timeout`——单次尝试超时，协作式：到期向尝试发出取消，executor 响应后产生 `408` 失败结果（宿主关闭仍然立即取消；不响应令牌的 executor 不会被强杀）
- `Retry`——有界指数退避（`MaxAttempts`/`InitialDelay`/`BackoffFactor`/`MaxDelay`）。重试覆盖：抛出的异常、job 超时、以及 `Retryable = true` 的失败结果。**未标 `Retryable` 的失败结果是终态**——executor 须按失败逐个 opt-in，防止持久性错误被重试掩盖

```csharp
options.Jobs.Add(new ScheduledJob
{
    Name = "rollup", ExecutorType = "rollup",
    Schedule = ScheduleMasks.EveryMinutes(10),
    Timeout = TimeSpan.FromMinutes(5),
    Retry = new JobRetryPolicy { MaxAttempts = 3, InitialDelay = TimeSpan.FromSeconds(2) }
});
```

重试是单次到点内的延迟兜底，不替代幂等设计；重试会整体重跑 `ExecuteAsync`——attempt 内的工作须保持幂等。

默认 `MemoryStateStore` 进程内且易失——last result 保留 7 天，custom state 支持可选过期。适合开发或可接受重启丢状态的单进程部署。持久化用 `PubSoft.Scheduling.EntityFrameworkCore` 并按生产 provider 验证。持久化本身不提供分布式 lease、原子读改写、集群互斥或 misfire 恢复。

## 护栏 Worker（Fenced Worker）

`AddPubSoftFencedWorker` 把"定时件先拿分布式锁再跑"的样板收口为一次注册——每个 tick 先获取具名 `IDistributedLock`，拿到锁才执行委托：

```csharp
services.AddPubSoftFencedWorker("reconcile-sweep", async (ctx, ct) =>
{
    // Redis 锁下 ctx.FencingToken 非空——传给被保护资源以拒绝过期写者
    await SweepAsync(ctx.FencingToken, ct);
}, o =>
{
    o.Interval = TimeSpan.FromMinutes(5);
    o.LockExpiry = TimeSpan.FromMinutes(10); // 必须长于最慢一次 tick
});
```

- `WaitTimeout` 默认为零——锁被占用即**跳过本 tick**（sweep 语义正确态），不会在别的实例后排队。
- tick 异常记日志后循环继续；`MaxExecutionTime` 会取消超时 tick。
- `IFencedExecutor` 同步注册，可直接注入用于调度 job 内或临时调用点的锁保护执行。直接调 `TryExecuteAsync` 同样默认不等待——要阻塞需显式传 `waitTimeout` 或 `Timeout.InfiniteTimeSpan`。
- 需先注册 `IDistributedLock`（单实例用进程内锁，多实例用 `PubSoft.Caching.Redis`）。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Scheduling`: `ExecutorRegistry` `IExecutorRegistry` `IJobExecutor` `IResultProcessor` `IResultProcessorRegistry` `IStateStore` `JobResult` `JobRetryPolicy` `LoggingResultProcessor` `MemoryStateStore` `ResultProcessorRegistry` `ScheduleMask` `ScheduleMasks` `ScheduledJob` `SchedulerEngine` `SchedulerHostedService` `SchedulerOptions` `SchedulerServiceCollectionExtensions`

## 验证

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~SchedulingTests
bash scripts/verify-package-consumer.sh
```
