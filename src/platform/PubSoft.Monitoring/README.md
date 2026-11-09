---
purpose: package-level hosting guide for PubSoft.Monitoring
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Monitoring

**English** | [简体中文](https://github.com/pubsoft-cn/pubsoft/blob/main/src/platform/PubSoft.Monitoring/README.zh-CN.md)


`PubSoft.Monitoring` supplies HTTP dependency probes, provider-neutral application health checks, and low-cardinality metrics. It does not expose endpoints automatically or choose an exporter; hosts opt in to the checks/endpoints they need and configure their metrics backend separately.

## HTTP Dependency Probe

Register `AddPubSoftMonitoring` and provide absolute HTTP(S) targets without URL-embedded credentials:

```csharp
using PubSoft.Monitoring;

services.AddPubSoftMonitoring(options =>
{
    options.ProbeTargets = ["https://catalog.internal/health", "https://billing.internal/health"];
    options.MinProbeSuccessRatio = 0.5;
    options.ProbeTimeout = TimeSpan.FromSeconds(2);
});
```

The `IProbeHealthCheck` issues sequential GET requests and considers 2xx responses successful. Its default per-target timeout is three seconds and default minimum success ratio is 0.5. An empty target list reports `ProbeHealth.Healthy` without making an external request; this is useful when no external dependency is configured, but it does not prove dependency availability. Caller cancellation propagates; individual timeouts and HTTP request failures count as failed targets. Avoid placing credentials in URLs; authentication, if required, belongs in an explicitly configured HTTP client handler.

## Application Checks

Register checks and the aggregator explicitly:

```csharp
services.AddPubSoftHealthCheck<DatabaseHealthCheck>();
services.AddPubSoftHealthCheck<QueueHealthCheck>();
services.AddPubSoftHealthChecks();
```

Implement `IAppHealthCheck` with a stable name and a cooperative cancellation-aware `CheckAsync`. `AddPubSoftHealthCheck<TCheck>` and the aggregators retain check instances as singletons; do not inject scoped dependencies such as a `DbContext` directly. If a check needs scoped services, inject `IServiceScopeFactory` and create/dispose a scope inside each `CheckAsync` call. The sequential `HealthCheckAggregator` preserves registration order; a thrown check becomes `Unhealthy` with a generic description, while caller cancellation propagates. Overall status is `Unhealthy` if any check is unhealthy, otherwise `Degraded` if any is degraded, otherwise `Healthy` (including when no checks are registered).

`AddPubSoftCachedHealthChecks` registers an alternative cached aggregator with single-flight refresh and explicit `InvalidateCache()`. Defaults are five seconds for Healthy and one second for Degraded/Unhealthy results. `AddPubSoftParallelHealthChecks` registers a parallel alternative, defaulting to a three-second timeout and maximum concurrency of eight. A timed-out check must cooperate with cancellation before its concurrency slot can be reused; checks that ignore cancellation can hold a slot until they finish. Choose one aggregator for the intended host wiring rather than registering competing aggregator types without an application-specific selector.

## Optional ASP.NET Endpoint

`PubSoft.AspNetCore` can expose only the overall status when explicitly mapped:

```csharp
using PubSoft.AspNetCore;

services.AddPubSoftHealthChecks();
app.MapPubSoftHealthEndpoint();
```

The default path is `/health`; `Unhealthy` returns 503 and `Healthy` or `Degraded` returns 200. The response includes only the overall status, not check names or descriptions. The host must register its checks and apply authorization/rate-limiting conventions on the returned endpoint builder if the deployment requires them.

## Metrics

Register `AddPubSoftMonitoringMetrics()` and inject the singleton `MonitoringMetrics` where instrumentation occurs. It records request counts by bounded HTTP status class, error counts by fixed category, request duration, and backend connection samples. Route, backend, URL, and free-form error strings are not metric tags. Connect the `PubSoft.Monitoring` meter to the host's chosen `System.Diagnostics.Metrics` listener/exporter; the package does not add an exporter dependency.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Monitoring`: `AppHealthCheckResult` `AppHealthStatus` `CachedHealthCheckAggregator` `HealthCheckAggregator` `HttpProbeHealthCheck` `IAppHealthCheck` `IProbeHealthCheck` `MonitoringErrorCategory` `MonitoringMetrics` `MonitoringOptions` `MonitoringServiceCollectionExtensions` `ParallelHealthCheckAggregator` `ProbeHealth`

<!-- pubsoft:api-surface:start -->

Machine-maintained public surface (regenerate: `dotnet run --project tools/PubSoft.DocsTool -- --sync`).

**Registration**

| Method |
|---|---|
| `AddPubSoftCachedHealthChecks` |
| `AddPubSoftHealthCheck` |
| `AddPubSoftHealthChecks` |
| `AddPubSoftMonitoring` |
| `AddPubSoftMonitoringMetrics` |
| `AddPubSoftParallelHealthChecks` |
| `AddPubSoftStartupDiagnostics` |
| `AddPubSoftStartupDiagnosticsHostedService` |

**(options & records)**

`Description`, `Detail`, `MinProbeSuccessRatio`, `ProbeTargets`, `ProbeTimeout`, `Status`

<!-- pubsoft:api-surface:end -->

## Verification

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~MonitoringTests
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~ApplicationHealthChecksTests
bash scripts/verify-package-consumer.sh
```

