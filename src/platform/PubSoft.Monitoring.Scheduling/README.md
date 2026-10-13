# PubSoft.Monitoring.Scheduling

**English** | [简体中文](README.zh-CN.md)

Scheduler-driven health probes: turns `PubSoft.Monitoring` check results into three-state transitions and throttled alerts, executed as `PubSoft.Scheduling` jobs.

## Registration

```csharp
services.AddPubSoftScheduler(/* job list */);
services.AddPubSoftMonitoringScheduling(options =>
{
    options.FailureThreshold = 3;
    options.RecoveryNoticeThrottle = TimeSpan.FromMinutes(15);
});
```

- Three-state outcomes (`Healthy`/`Degraded`/`Unhealthy`) — a state change requires the configured number of consecutive failures
- Recovery notifications are throttled so a flapping probe cannot spam channels
- Same-job executions serialize within one processor instance; multi-instance deployments need an external lock (`IDistributedLock`)
- Alerts dispatch through `PubSoft.Notifications` channel contracts — register channels separately

## Boundaries

Probe contracts, the health aggregator, and bounded-parallelism options live in [PubSoft.Monitoring](../../platform/PubSoft.Monitoring/README.md). Job scheduling semantics live in [PubSoft.Scheduling](../../platform/PubSoft.Scheduling/README.md).

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Monitoring.Scheduling`: `MonitoringSchedulingOptions` `ProbeExecutionResult` `ProbeFailureState` `ProbeOutcome` `ProbeState` `ProbeStateService` `ScheduledProbeExecutor` `ProbeAlertRuleProcessor` `MonitoringSchedulingServiceCollectionExtensions`
