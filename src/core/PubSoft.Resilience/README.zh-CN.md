---
purpose: PubSoft.Resilience 包级接入指南
audience: 集成方宿主开发者
owner: PubSoft 维护组
last-reviewed: 2026-10-13
---

# PubSoft.Resilience

[English](README.md) | **简体中文**

Provider 中立的韧性原语，无框架依赖——`PubSoft.Resilience.Http` 与 `PubSoft.Scheduling.Http` 构建于其上的策略层。

## 核心概念

**`RetryPolicy`**——有界重试，单次尝试与总超时预算分离：

```csharp
var policy = new RetryPolicy
{
    MaxRetries = 2,                          // 首次尝试之后的重试次数
    InitialDelay = TimeSpan.FromMilliseconds(200),
    JitterRatio = 0.2,
    RetryOn5xx = true,
    RetryOnConnectionFailure = true,
    RetryOnTimeout = true
};
```

- `GetDelay` 返回确定性指数基础退避、上限 30 秒。使用 `GetDelayWithJitter` 的执行器会在 `JitterRatio`（默认 ±20%）范围内对称采样，并保持同样 30 秒上限；所有延迟消耗总超时预算
- `MaxRetries` 计重试而非总尝试次数（`MaxRetries=2` → 至多执行 3 次）
- HTTP 408/429 响应重试为显式 opt-in（`RetryOnRequestTimeout` / `RetryOnTooManyRequests`）；其他 4xx 永不重试
- 调用方取消正常传播，绝不视为可重试失败

**`CircuitBreaker`**——单进程失败阈值熔断，半开探测：

```csharp
var breaker = new CircuitBreaker
{
    Options = new CircuitBreakerOptions
    {
        FailureThreshold = 5,
        OpenTimeout = TimeSpan.FromSeconds(30)
    }
};
```

- 成功关闭/重置；失败、重试耗尽、总超时失败计入阈值
- 默认模式按连续失败 streak 熔断。`CircuitBreakerMode.RollingFailureRatio` 是面向噪声流量的显式 opt-in：只有 `SamplingDuration` 内样本数至少达到 `MinimumThroughput` 且失败占比达到 `FailureRatio` 才熔断，可降低少量 429/5xx 混入整体健康流量时的误杀
- 打开的 breaker 立即拒绝——不发起请求
- 调用方取消释放半开探针，取消的请求不会把 breaker 卡死
- 状态在进程内、按 breaker 实例分键——非分布式协调机制

## 语义与边界

- 失败分类归调用方——库不猜测哪些异常是瞬时的
- 无自动应用级幂等分析：重试安全规则在适配器层（`Resilience.Http` 直接拒绝非安全方法）
- 所有异步 API 传播 `CancellationToken`

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Resilience`: `CircuitBreaker` `CircuitBreakerOptions` `CircuitState` `RetryExecution` `RetryPolicy` `RetryResult`
