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

## 出口管控（SSRF）

任务 URL 受约束，低信任的任务写入方无法把调度器引向内网或云元数据端点：

- `HttpExecutorOptions.BlockPrivateNetworks`（默认 **true**）——拒绝解析到非公网
  地址的目标（RFC1918、loopback、link-local 含云元数据 `169.254.x.x`、CGNAT、组播、
  保留/过渡段）。检查在**连接时**执行：`ConnectCallback` 自行解析并固定地址，
  关闭了"校验到连接之间换 DNS 答案"的 rebinding 窗口。
- `HttpExecutorOptions.AllowedHosts`（默认空 = 不设白名单）——精确主机
  （`api.example.com`）或后缀通配（`.example.com` / `*.example.com`）。
  需要严格出口隔离时配置。
- `HttpExecutorOptions.FollowRedirects`（默认 **false**）——开启后由执行器手工
  跟随跳转（上限 8 跳）：每跳重新走 scheme/白名单/私网校验，跨主机时剥除
  `TokenRef` 凭据头；301/302/303 时 `POST` 降级为 `GET`（与 HttpClient 一致）。
- `HttpExecutorOptions.UseSystemProxy`（默认 **false**）——环境/系统代理会绕过
  连接时检查，默认直连；options 校验**拒绝** `UseSystemProxy` 与
  `BlockPrivateNetworks` 同开的组合。

仅当任务确需探测宿主自身内网（如内网健康检查）时才将 `BlockPrivateNetworks` 置 false。

## 边界

job 注册、bitmask 排程、引擎上限与 state store 在 [PubSoft.Scheduling](../../platform/PubSoft.Scheduling/README.zh-CN.md)。本包只加执行器。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Scheduling.Http`: `HttpExecutorOptions` `HttpProbeExecutor` `HttpResilientExecutor` `ScheduledHttpRequest` `ScheduledHttpResult` `SchedulingHttpServiceCollectionExtensions`
重定向逐跳手工跟随并重复目标校验。当跳转改变 origin（scheme/host/port）时，任务配置请求头与 token 头不再转发——凭证绝不跨源发送。

