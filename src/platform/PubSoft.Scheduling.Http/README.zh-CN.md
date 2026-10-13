# PubSoft.Scheduling.Http

[English](README.md) | **简体中文**

`PubSoft.Scheduling` job 的 HTTP 执行器——无 ASP.NET Core 依赖。

## 注册

```csharp
services.AddPubSoftHttpExecutors();
// 然后配置 job：ExecutorType 为 "Http.Probe" 或 "Http.Resilient"
```

## 执行器

| 执行器 | 行为 | 用于 |
|---|---|---|
| `Http.Probe` | 单次请求；校验 URL/header/timeout + 预期状态码；绝不重试 | 健康/存活检查 |
| `Http.Resilient` | `PubSoft.Resilience` 重试/熔断策略；异步 token 获取访问需认证目标 | 调内部 HTTP 端点 |

`Http.Resilient` 默认只重试无 body 的 `GET`/`HEAD`/`OPTIONS`/`TRACE`；其他方法或带 content 请求须宿主显式 `AllowUnsafeRetries = true`，前提是已确认端点幂等。token、URL、凭据不进日志；错误面已脱敏。

## 边界

job 注册、bitmask 排程、引擎上限与 state store 在 [PubSoft.Scheduling](../../platform/PubSoft.Scheduling/README.zh-CN.md)。本包只加执行器。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Scheduling.Http`: `HttpExecutorOptions` `HttpProbeExecutor` `HttpResilientExecutor` `ScheduledHttpRequest` `ScheduledHttpResult` `SchedulingHttpServiceCollectionExtensions`
