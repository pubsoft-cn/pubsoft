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

处理器从每个 job 的 `ResultProcessorConfig` JSON 读取规则。`Condition` 与 `Severity` 接受枚举成员名（`"StateConfirmed"`、`"Critical"` 等）或数值；某条规则无法解析或不合法时，整组规则失效并记日志（含规则索引与原因）。`AddPubSoftMonitoringScheduling` 还会在宿主启动时对每个声明了 `AlertRules` 的 job 重跑同样的校验——配错在部署期即报，不必等 job 首次执行。

```json
"ResultProcessorConfig": {
  "Rules": [{
    "Name": "confirmed",
    "Condition": "StateConfirmed",
    "Severity": "Critical",
    "Channels": ["dingtalk", "sms"],
    "Recipients": ["+1234567890"],
    "TemplateCode": "ALERT_DOWN",
    "TemplateParameters": { "code": "8000" },
    "Throttle": "00:15:00"
  }]
}
```

`Recipients`、`TemplateCode` 与 `TemplateParameters` 透传至派发的 `NotificationMessage`——消费它们的通道（如短信通道）无需宿主侧兜底配置，不消费的通道不受影响。`TemplateParameters` 为静态参数；每条消息还会自动携带动态内置参数 `job`、`status`、`state`、`severity`，声明条目与内置 key 冲突时以声明为准。字符串类型的规则字段原样保留配置文本——绑定的 `"8000"` 到达时仍是 `"8000"`，不会变成 JSON 数字。

## 环境出网故障看门狗

默认关闭；开启必须至少配置一个通道（否则启动校验报错）。探针目标不可达期间，job 告警被抑制、故障本身无人知晓——开启 `options.Environment.Enabled` 后，处理器记录故障窗口，并在探针恢复后通过 `Environment.Channels` 派发一次摘要：

```csharp
services.AddPubSoftMonitoringScheduling(options =>
{
    options.Environment = new EnvironmentAlertOptions
    {
        Enabled = true,
        MinUnreachableDuration = TimeSpan.FromMinutes(2),
        Channels = ["dingtalk"],
        Severity = AlertSeverity.Critical
    };
});
```

- 告警在**恢复时刻**发出——出网中断期间同一条路径也发不出去。消息携带故障窗口与被抑制的 job 执行次数（另以 `windowStart`/`windowMinutes`/`suppressedCount` 模板参数暴露）。
- `SendImmediate` 会在首次观测到故障时额外尝试一次即时通知——出网断开期间通常发不出去，但半通场景能提前送达。每个故障窗口最多尝试一次。即时探测**按设计就是 best-effort**——失败后永不重试（它被恢复摘要严格取代），而恢复摘要与 job 告警一样受 `PendingAlerts` 覆盖。即时与摘要使用不同的 correlation id，即时送达成功不会吞掉摘要。
- 边界：检测粒度为 job 执行频率——摘要在探针恢复后的首次 job 执行时发出。抑制计数按 processor 实例观测。短于 `MinUnreachableDuration` 的中断只记日志不告警。

## 投递持久性

告警投递按通道 best-effort：规则在条件成立的执行上触发，转移边沿型规则（`StateConfirmed`、`StateRecovered` 等）只触发一次——若那一刻所有通道都发不出去，该告警永久丢失。探针抑制在中断持续期间不消费转移（健康状态机在探针不可达期间冻结、恢复后续算），所以丢失窗口在**通道侧失败**，不在出网中断。有一个刻意的例外：**探针恢复后首次执行**上触发的恢复边沿（`StateRecovered`/`Recovered`）会被抑制并消费、不重放——冻结期间不会记录新的劣化，所以这种边沿几乎总是在报告已被环境摘要覆盖的中断假象，而非独立确认的恢复。抑制消费边沿时会输出 information 级日志。

`options.PendingAlerts` 为需要的部署闭合该窗口（opt-in，默认关闭）。job 规则告警与环境中断摘要同样覆盖（摘要持久化在固定伪 job key 下）；best-effort 即时探测刻意不覆盖：

- 无通道送达的告警按通道持久化，在后续 job 执行上重试，直到送达或超过 `Expiry`（默认 24h）；`MaxEntries`（默认 50）限制单 job 积压，最旧的先丢。
- 补发保留原始 `CorrelationId`，与后续正常触发的重叠由 dispatcher 去重坍缩。
- 边界：pending 状态存在 job 的 `IStateStore` 中——持久性取决于 store 实现（内存 store 重启即丢）。多实例部署由下一个执行的实例补发；重复发送仅在去重窗口内被坍缩。

## 边界

按 job 的告警处理经由进程内锁表串行化，容量有界——job 名必须是有界固定集合；超限响亮抛错而非静默削弱重叠隔离。探针契约、健康聚合器与有界并行选项在 [PubSoft.Monitoring](../../platform/PubSoft.Monitoring/README.zh-CN.md)；job 调度语义在 [PubSoft.Scheduling](../../platform/PubSoft.Scheduling/README.zh-CN.md)。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Monitoring.Scheduling`: `AlertConditionType` `AlertContext` `AlertProcessorOptions` `AlertRule` `AlertRuleConfig` `AlertRuleProcessor` `AlertSeverity` `JobHealthState` `MonitoringSchedulingServiceCollectionExtensions`
