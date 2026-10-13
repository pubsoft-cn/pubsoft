# PubSoft.Monitoring.Scheduling

[English](README.md) | **简体中文**

调度驱动的健康探测：把 `PubSoft.Monitoring` 检查结果转为三态迁移与节流告警，作为 `PubSoft.Scheduling` job 执行。

## 注册

```csharp
services.AddPubSoftScheduler(/* job 列表 */);
services.AddPubSoftMonitoringScheduling(options =>
{
    options.FailureThreshold = 3;
    options.RecoveryNoticeThrottle = TimeSpan.FromMinutes(15);
});
```

- 三态结果（`Healthy`/`Degraded`/`Unhealthy`）——状态迁移要求连续失败达配置阈值
- 恢复通知节流——抖动的探针不会刷告警通道
- 同 job 在单 processor 实例内串行；多实例部署需外部锁（`IDistributedLock`）
- 告警经 `PubSoft.Notifications` 通道契约分发——通道另行注册

## 边界

探针契约、健康聚合器与有界并行选项在 [PubSoft.Monitoring](../../platform/PubSoft.Monitoring/README.zh-CN.md)；job 调度语义在 [PubSoft.Scheduling](../../platform/PubSoft.Scheduling/README.zh-CN.md)。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Monitoring.Scheduling`: `MonitoringSchedulingOptions` `ProbeExecutionResult` `ProbeFailureState` `ProbeOutcome` `ProbeState` `ProbeStateService` `ScheduledProbeExecutor` `ProbeAlertRuleProcessor` `MonitoringSchedulingServiceCollectionExtensions`
