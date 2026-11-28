---
purpose: PubSoft.Resilience.Http 包级接入指南
audience: 集成方宿主开发者
owner: PubSoft 维护组
last-reviewed: 2026-10-13
---

# PubSoft.Resilience.Http

[English](README.md) | **简体中文**

`PubSoft.Resilience.Http` 为具名 `HttpClient` 提供有界超时、重试与可选熔断。handler 不重试非安全方法或带 content 的请求，且不存在 unsafe-retry 覆盖开关。

## 注册

```csharp
using PubSoft.Resilience;
using PubSoft.Resilience.Http;

services.AddHttpClient("catalog", client =>
    client.BaseAddress = new Uri("https://catalog.internal/"))
    .AddPubSoftHttpResilience(options =>
    {
        options.Timeout = TimeSpan.FromSeconds(8);
        options.PerAttemptTimeout = TimeSpan.FromSeconds(3);
        options.Retry = new RetryPolicy
        {
            MaxRetries = 2,
            InitialDelay = TimeSpan.FromMilliseconds(200),
            RetryOn5xx = true,
            RetryOnConnectionFailure = true,
            RetryOnTimeout = true
        };
        options.CircuitBreaker = new CircuitBreakerOptions
        {
            FailureThreshold = 5,
            OpenTimeout = TimeSpan.FromSeconds(30)
        };
    });
```

`IConfiguration` 重载从配置节绑定同样的选项——逐键显式绑定，不依赖反射式配置绑定器（AOT 安全）。时长一律用扁平化的毫秒键：

| 键 | 映射到 |
|---|---|
| `TimeoutMs` | `Timeout` |
| `PerAttemptTimeoutMs` | `PerAttemptTimeout` |
| `Retry:MaxRetries` / `Retry:InitialDelayMs` / `Retry:JitterRatio` / `Retry:RetryOnTimeout` / `Retry:RetryOn5xx` / `Retry:RetryOnRequestTimeout` / `Retry:RetryOnTooManyRequests` / `Retry:RetryOnConnectionFailure` | `Retry` 同名成员 |
| `CircuitBreaker:Mode` / `CircuitBreaker:FailureThreshold` / `CircuitBreaker:FailureRatio` / `CircuitBreaker:MinimumThroughput` / `CircuitBreaker:SamplingDurationMs` / `CircuitBreaker:OpenTimeoutMs` | `CircuitBreaker` 同名成员 |

```csharp
services.AddHttpClient("catalog", client =>
    client.BaseAddress = new Uri("https://catalog.internal/"))
    .AddPubSoftHttpResilience(configuration.GetSection("Resilience"));
```

默认总超时 8 秒、单次尝试 5 秒；单次不得超总超时。handler 禁用 `HttpClient.Timeout` 并接管超时预算。调用方取消正常传播，不被当作可重试超时。

## 重试安全

重试仅限无 content 且方法为 GET/HEAD/OPTIONS/TRACE 的请求。重试策略可选 5xx、连接失败与超时；HTTP 408/429 重试须显式开启 `RetryOnRequestTimeout` / `RetryOnTooManyRequests`，其他 4xx 不重试。`MaxRetries` 计首次尝试之后的重试次数。退避自 `InitialDelay` 翻倍、上限 30 秒，handler 会叠加有界对称 jitter（`JitterRatio`，默认 ±20%）。可重试响应上的合法 `Retry-After` 会作为最小等待时间：实际等待取 jitter 后退避与 `Retry-After` 的较大值；若该最小值连总超时预算都无法容纳，handler 直接返回响应而不再重试。所有延迟仍消耗总超时预算。中间重试响应会被 dispose。

方法/请求体安全检查不能确立应用级幂等——只对语义上可安全重复的端点启用重试。本 handler 绝不重试 POST/PUT/DELETE 及任何带 content 请求，即使配置了重试策略；非安全操作需要重放时请做单独评审的设计，不要假设可通过选项覆盖此 handler。

## 熔断器

配置后按请求 host 分键。成功响应关闭/重置该 host 失败计数；失败响应、transport/超时耗尽与总超时失败计入阈值。HTTP 5xx、408、429 计为失败；其他 4xx 视为远端仍在正常响应，不计为故障。默认连续计数模式刻意保持简单。会产生噪声型 429 突发的高流量客户端（例如调用方错误死循环）应显式启用 `CircuitBreakerMode.RollingFailureRatio` 并调优 `FailureRatio`、`MinimumThroughput`、`SamplingDuration`；这样既降低 host 级误杀，也不掩盖持续过半的失败。打开的 breaker 以 `HttpRequestException` 拒绝请求。调用方取消会释放半开探针，取消的请求不会把 breaker 卡在半开态。breaker 状态在进程内、绑定 handler 的 `CircuitBreaker` 实例——非分布式协调机制。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Resilience.Http`: `HttpResilienceOptions` `HttpResilienceServiceCollectionExtensions` `PubSoftHttpResilienceHandler`

## 验证

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~HttpResilienceHandlerTests
bash scripts/verify-package-consumer.sh
```
