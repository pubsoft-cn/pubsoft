---
purpose: PubSoft.Monitoring.Scheduling 包级接入指南
audience: 集成方宿主开发者
owner: PubSoft 维护组
last-reviewed: 2026-10-13
---

# PubSoft.Monitoring.Scheduling

[English](README.md) | **简体中文**

调度驱动的健康探测：把 `PubSoft.Monitoring` 检查结果转为三态迁移与节流告警，作为 `PubSoft.Scheduling` job 执行。

## 注册

```csharp
services.AddPubSoftScheduler(/* job 列表 */);
services.AddPubSoftMonitoringScheduling(options =>
{
    options.FailureThreshold = 3;
});
```

- 三态结果（`Healthy`/`Degraded`/`Unhealthy`）——状态迁移要求连续失败达配置阈值
- 恢复通知节流——抖动的探针不会刷告警通道
- 同 job 在单 processor 实例内串行；多实例部署需外部锁（`IDistributedLock`）
- 告警经 `PubSoft.Notifications` 通道契约分发——通道另行注册

## 告警规则

处理器从每个 job 的 `ResultProcessorConfig` JSON 读取规则。`Condition` 与 `Severity` 接受枚举成员名（`"StateConfirmed"`、`"Critical"` 等）或数值；某条规则无法解析或不合法时，整组规则失效并记日志（含规则索引与原因）。

```json
"ResultProcessorConfig": {
  "Rules": [{
    "Name": "confirmed",
    "Condition": "StateConfirmed",
    "Severity": "Critical",
    "Channels": ["dingtalk", "sms"],
    "Recipients": ["+1234567890"],
    "TemplateCode": "ALERT_DOWN",
    "Throttle": "00:15:00"
  }]
}
```

`Recipients` 与 `TemplateCode` 透传至派发的 `NotificationMessage`——消费它们的通道（如短信通道）无需宿主侧兜底配置，不消费的通道不受影响。

## 边界

探针契约、健康聚合器与有界并行选项在 [PubSoft.Monitoring](../../platform/PubSoft.Monitoring/README.zh-CN.md)；job 调度语义在 [PubSoft.Scheduling](../../platform/PubSoft.Scheduling/README.zh-CN.md)。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Monitoring.Scheduling`: `AlertConditionType` `AlertContext` `AlertProcessorOptions` `AlertRule` `AlertRuleConfig` `AlertRuleProcessor` `AlertSeverity` `JobHealthState` `MonitoringSchedulingServiceCollectionExtensions`
