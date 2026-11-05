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

默认总超时 8 秒、单次尝试 5 秒；单次不得超总超时。handler 禁用 `HttpClient.Timeout` 并接管超时预算。调用方取消正常传播，不被当作可重试超时。

## 重试安全

重试仅限无 content 且方法为 GET/HEAD/OPTIONS/TRACE 的请求。重试策略可选 5xx、连接失败与超时；4xx 不重试。`MaxRetries` 计首次尝试之后的重试次数。退避自 `InitialDelay` 翻倍、上限 30 秒；所有延迟都消耗总超时预算。中间重试响应会被 dispose。

方法/请求体安全检查不能确立应用级幂等——只对语义上可安全重复的端点启用重试。本 handler 绝不重试 POST/PUT/DELETE 及任何带 content 请求，即使配置了重试策略；非安全操作需要重放时请做单独评审的设计，不要假设可通过选项覆盖此 handler。

## 熔断器

配置后按请求 host 分键。成功响应关闭/重置该 host 失败计数；失败响应、transport/超时耗尽与总超时失败计入阈值。打开的 breaker 以 `HttpRequestException` 拒绝请求。调用方取消会释放半开探针，取消的请求不会把 breaker 卡在半开态。breaker 状态在进程内、绑定 handler 的 `CircuitBreaker` 实例——非分布式协调机制。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Resilience.Http`: `HttpResilienceOptions` `HttpResilienceServiceCollectionExtensions` `PubSoftHttpResilienceHandler`

## 验证

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~HttpResilienceHandlerTests
bash scripts/verify-package-consumer.sh
```
