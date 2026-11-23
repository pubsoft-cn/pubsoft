---
purpose: package-level hosting guide for PubSoft.Monitoring.Scheduling
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Monitoring.Scheduling

**English** | [简体中文](https://github.com/pubsoft-cn/pubsoft/blob/main/src/platform/PubSoft.Monitoring.Scheduling/README.zh-CN.md)

Scheduler-driven health probes: turns `PubSoft.Monitoring` check results into three-state transitions and throttled alerts, executed as `PubSoft.Scheduling` jobs.

## Registration

```csharp
services.AddPubSoftScheduler(/* job list */);
services.AddPubSoftMonitoringScheduling(options =>
{
    options.FailureThreshold = 3;
});
```

- Three-state outcomes (`Healthy`/`Degraded`/`Unhealthy`) — a state change requires the configured number of consecutive failures
- Recovery notifications are throttled so a flapping probe cannot spam channels
- Same-job executions serialize within one processor instance; multi-instance deployments need an external lock (`IDistributedLock`)
- Alerts dispatch through `PubSoft.Notifications` channel contracts — register channels separately

## Alert rules

The processor reads rules from each job's `ResultProcessorConfig` JSON. `Condition` and `Severity` take enum member names (`"StateConfirmed"`, `"Critical"`, ...) or their numeric values; an unreadable or invalid rule fails the whole rule set with a log naming the rule index and reason.

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

`Recipients` and `TemplateCode` pass through to the dispatched `NotificationMessage` — channels that honor them (e.g. an SMS channel) can consume them without host-side fallback config; channels that ignore them are unaffected.

## Boundaries

Probe contracts, the health aggregator, and bounded-parallelism options live in [PubSoft.Monitoring](../../platform/PubSoft.Monitoring/README.md). Job scheduling semantics live in [PubSoft.Scheduling](../../platform/PubSoft.Scheduling/README.md).

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Monitoring.Scheduling`: `AlertConditionType` `AlertContext` `AlertProcessorOptions` `AlertRule` `AlertRuleConfig` `AlertRuleProcessor` `AlertSeverity` `JobHealthState` `MonitoringSchedulingServiceCollectionExtensions`

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**Registration**

| Method |
|---|---|
| `AddPubSoftMonitoringScheduling` |

**(options & records)**

`Channels`, `Condition`, `Current`, `DelayThresholdMs`, `FailureThreshold`, `FromState`, `ProbeHealth`, `Recipients`, `Rules`, `Severity`, `TemplateCode`, `Throttle`, `ToState`, `WasProbeUnreachable`

<!-- pubsoft:api-surface:end -->

