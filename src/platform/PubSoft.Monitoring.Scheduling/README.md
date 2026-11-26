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

The processor reads rules from each job's `ResultProcessorConfig` JSON. `Condition` and `Severity` take enum member names (`"StateConfirmed"`, `"Critical"`, ...) or their numeric values; an unreadable or invalid rule fails the whole rule set with a log naming the rule index and reason. `AddPubSoftMonitoringScheduling` also re-runs the same validation at host startup over every job that lists `AlertRules`, so a bad rule set surfaces at deploy time instead of the job's first execution.

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

`Recipients`, `TemplateCode` and `TemplateParameters` pass through to the dispatched `NotificationMessage` — channels that honor them (e.g. an SMS channel) can consume them without host-side fallback config; channels that ignore them are unaffected. `TemplateParameters` entries are static; every message additionally carries the built-in dynamic parameters `job`, `status`, `state` and `severity`, and a declared entry overrides the built-in value on a key collision. String-typed rule fields keep their configuration text verbatim — a bound `"8000"` arrives as `"8000"`, never a JSON number.

## Environment egress watchdog

Disabled by default; enabling requires at least one channel (startup validation fails otherwise). While probe targets are unreachable, job alerts are suppressed and the outage itself would go unnoticed — with `options.Environment.Enabled`, the processor records the outage window and dispatches one summary through `Environment.Channels` after the probe recovers:

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

- The alert fires at **recovery** time — while egress is down, the same path cannot deliver it. The message carries the outage window and the number of suppressed job executions (also exposed as `windowStart`/`windowMinutes`/`suppressedCount` template parameters).
- `SendImmediate` additionally attempts one early notification when the outage is first observed; it usually fails while egress is down but lands early in partial-connectivity cases. At most one attempt per outage window. The immediate probe is best-effort **by design** — a failure is never retried (it is strictly superseded by the recovery summary) — while the recovery summary is covered by `PendingAlerts` like any job alert. Immediate and summary use distinct correlation ids, so a successful immediate never suppresses the summary.
- Boundaries: detection granularity is the job execution cadence — the summary goes out on the first job execution after the probe recovers. The suppressed-executions count is observed per processor instance. Outages shorter than `MinUnreachableDuration` are logged, not alerted.

## Delivery durability

Alert dispatch is best-effort per channel: a rule fires on the execution where its condition holds, and a transition-edge rule (`StateConfirmed`, `StateRecovered`, ...) fires exactly once — if every channel send fails at that moment, the alert is lost. Probe suppression does not consume transitions while the outage lasts (the health state machine freezes while the probe is unreachable and resumes on recovery), so the loss window is channel-side failures, not egress outages. One deliberate exception: a recovery edge (`StateRecovered`/`Recovered`) firing on the **first execution after probe restoration** is inhibited and consumed, not replayed — during the freeze no new degradation can be recorded, so such an edge almost always reports an outage artifact already covered by the environment summary rather than an independently confirmed recovery. The inhibition is logged at information level when it consumes an edge.

`options.PendingAlerts` closes that window for deployments that need it (opt-in, disabled by default). It covers job rule alerts and the environment outage summary alike (the summary is persisted under a fixed pseudo-job key); the best-effort immediate probe stays uncovered on purpose:

- An alert no channel could deliver is persisted per channel and retried on later job executions until it lands or `Expiry` (default 24 h) passes; `MaxEntries` (default 50) caps the per-job backlog, oldest dropped first.
- A per-job backlog retries at the cadence of its owning job's own executions. The shared environment backlog instead retries at most once per `RetryInterval` (default one minute) across all jobs — without that cap every job execution would retry it, multiplying the resend rate and turning a persistently failing channel into a poison-message loop. `RetryInterval` of zero restores flush-on-every-execution.
- Resends keep the original `CorrelationId`, so dispatcher deduplication collapses an overlap with a later normal fire.
- Boundaries: pending state lives in the job's `IStateStore` — durability follows the store implementation (the in-memory store loses pendings on restart). Multi-instance deployments resend from whichever instance executes next; duplicates are collapsed by deduplication only inside the dedup window.

## Boundaries

Per-job alert processing is serialized through an in-process lock table of bounded capacity; job names must be a bounded fixed set — exceeding the cap throws loudly instead of silently weakening overlap isolation. Probe contracts, the health aggregator, and bounded-parallelism options live in [PubSoft.Monitoring](../../platform/PubSoft.Monitoring/README.md). Job scheduling semantics live in [PubSoft.Scheduling](../../platform/PubSoft.Scheduling/README.md).

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

`Channels`, `Condition`, `Current`, `DelayThresholdMs`, `Enabled`, `Environment`, `Expiry`, `FailureThreshold`, `FromState`, `MaxEntries`, `MinUnreachableDuration`, `PendingAlerts`, `ProbeHealth`, `Recipients`, `RetryInterval`, `Rules`, `SendImmediate`, `Severity`, `TemplateCode`, `TemplateParameters`, `Throttle`, `ToState`, `WasProbeUnreachable`

<!-- pubsoft:api-surface:end -->

