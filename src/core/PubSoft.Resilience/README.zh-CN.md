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
    RetryOn5xx = true,
    RetryOnConnectionFailure = true,
    RetryOnTimeout = true
};
```

- 退避自 `InitialDelay` 翻倍、上限 30 秒；所有延迟消耗总超时预算
- `MaxRetries` 计重试而非总尝试次数（`MaxRetries=2` → 至多执行 3 次）
- 调用方取消正常传播，绝不视为可重试失败

**`CircuitBreaker`**——单进程失败阈值熔断，半开探测：

```csharp
var breaker = new CircuitBreaker(new CircuitBreakerOptions
{
    FailureThreshold = 5,
    OpenTimeout = TimeSpan.FromSeconds(30)
});
```

- 成功关闭/重置；失败、重试耗尽、总超时失败计入阈值
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
