# PubSoft.Monitoring

[English](README.md) | **简体中文**

`PubSoft.Monitoring` 提供 HTTP 依赖探针、provider 中立的应用健康检查与低基数指标。不自动暴露端点、不选择 exporter——宿主按需 opt-in 检查/端点并单独配置指标后端。

## HTTP 依赖探针

注册 `AddPubSoftMonitoring` 并提供无内嵌凭据的绝对 HTTP(S) 目标：

```csharp
using PubSoft.Monitoring;

services.AddPubSoftMonitoring(options =>
{
    options.ProbeTargets = ["https://catalog.internal/health", "https://billing.internal/health"];
    options.MinProbeSuccessRatio = 0.5;
    options.ProbeTimeout = TimeSpan.FromSeconds(2);
});
```

`IProbeHealthCheck` 顺序发起 GET，2xx 视为成功。默认每目标超时 3 秒、最低成功率 0.5。空目标列表不发外部请求直接报 `ProbeHealth.Healthy`——适合未配置外部依赖时，但不证明依赖可用。调用方取消传播；单目标超时与请求失败计为失败目标。URL 中勿放凭据——需要认证时配置显式 HttpClient handler。

## 应用检查

显式注册检查与聚合器：

```csharp
services.AddPubSoftHealthCheck<DatabaseHealthCheck>();
services.AddPubSoftHealthCheck<QueueHealthCheck>();
services.AddPubSoftHealthChecks();
```

实现 `IAppHealthCheck`：稳定名称 + 协作式取消感知 `CheckAsync`。`AddPubSoftHealthCheck<TCheck>` 与聚合器以单例持有检查实例——不要直接注入 `DbContext` 等 scoped 依赖；需要 scoped 服务时注入 `IServiceScopeFactory` 并在每次 `CheckAsync` 内创建/释放 scope。顺序 `HealthCheckAggregator` 保持注册顺序；抛异常的检查记为 `Unhealthy` + 通用描述，调用方取消传播。任一检查 Unhealthy 则总体 Unhealthy，否则任一 Degraded 则 Degraded，否则 Healthy（无注册检查时亦 Healthy）。

`AddPubSoftCachedHealthChecks` 注册缓存聚合器：single-flight 刷新 + 显式 `InvalidateCache()`，默认 Healthy 5 秒、Degraded/Unhealthy 1 秒。`AddPubSoftParallelHealthChecks` 注册并行聚合器：默认 3 秒超时、最大并发 8；超时检查须协作响应取消才能释放并发槽——忽略取消的检查会占槽至完成。按宿主接线选一个聚合器，不要无应用级选择器就并列注册多个。

## 可选 ASP.NET 端点

`PubSoft.AspNetCore` 显式映射时仅暴露总体状态：

```csharp
using PubSoft.AspNetCore;

services.AddPubSoftHealthChecks();
app.MapPubSoftHealthEndpoint();
```

默认路径 `/health`；`Unhealthy`→503，`Healthy`/`Degraded`→200。响应只含总体状态，不含检查名/描述。部署需要时宿主在返回的 endpoint builder 上配置授权与限流。

## 指标

注册 `AddPubSoftMonitoringMetrics()` 并在插桩点注入单例 `MonitoringMetrics`。记录按有界 HTTP 状态类的请求计数、按固定类别的错误计数、请求延迟与后端连接样本。route、backend、URL 与自由文本错误串不进指标标签。把 `PubSoft.Monitoring` meter 接入宿主选择的 `System.Diagnostics.Metrics` listener/exporter——本包不附带 exporter 依赖。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Monitoring`: `AppHealthCheckResult` `AppHealthStatus` `CachedHealthCheckAggregator` `HealthCheckAggregator` `HttpProbeHealthCheck` `IAppHealthCheck` `IProbeHealthCheck` `MonitoringErrorCategory` `MonitoringMetrics` `MonitoringOptions` `MonitoringServiceCollectionExtensions` `ParallelHealthCheckAggregator` `ProbeHealth`

## 验证

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~MonitoringTests
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~ApplicationHealthChecksTests
bash scripts/verify-package-consumer.sh
```
