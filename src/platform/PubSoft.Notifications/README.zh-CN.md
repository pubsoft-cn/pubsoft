---
purpose: PubSoft.Notifications 包级接入指南
audience: 集成方宿主开发者
owner: PubSoft 维护组
last-reviewed: 2026-10-13
---

# PubSoft.Notifications

[English](README.md) | **简体中文**

`PubSoft.Notifications` 提供不可变通知消息、通道契约、分发结果、有界进程内去重，以及两个内置 webhook 通道（`teams`、`dingtalk`）。不含持久投递或跨实例去重；厂商专有集成（短信/邮件/推送）有意留给宿主以自有 `INotificationChannel` 实现。

## 注册

注册各通道并配置 dispatcher：

```csharp
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Notifications;

// 内置通道按配置节注册（子节 Teams / DingTalk；子节不存在则不注册该通道）：
services.AddPubSoftNotificationChannels(configuration.GetSection("Notifications"));

// 或逐通道注册：
services.AddTeamsNotificationChannel(configuration.GetSection("Notifications:Teams"));
services.AddDingTalkNotificationChannel(options => { options.WebhookUrl = webhookUrl; });

// 自定义通道：
services.AddSingleton<INotificationChannel, EmailNotificationChannel>();
services.AddPubSoftNotifications(options =>
{
    options.DeduplicationWindow = TimeSpan.FromMinutes(5);
    options.MaximumDeduplicationEntries = 10_000;
});
```

内置通道的 HTTP 调用走命名 `IHttpClientFactory` 客户端 + `PubSoft.Resilience.Http` 默认策略（总超时 10s、单次 5s、重试 1 次）。Webhook URL 须为绝对 `https`；非 loopback `http` 在 options 校验期拒绝，loopback 为本地开发豁免，确需例外可逐通道 `AllowInsecureUrl` 显式放行。

`AddPubSoftNotifications` 将 `NotificationDispatcher` 注册为单例——通道实例解析一次即长期持有，通道实现及其依赖必须可安全用于单例；HTTP 集成请用 `IHttpClientFactory`，勿向通道注入 scoped 服务。通道名必须非空且大小写不敏感唯一。

## 分发

按所选 provider 需要构造消息——内容、接收者、模板参数复制为不可变快照：

```csharp
var message = new NotificationMessage(
    content: "Your report is ready.",
    recipients: ["user@example.test"],
    title: "Report complete",
    templateCode: "report-ready-v1",
    templateParameters: new Dictionary<string, string> { ["reportId"] = reportId },
    source: "reporting",
    correlationId: $"report-ready:{reportId}");

var result = await dispatcher.DispatchAsync("email", message, cancellationToken);
if (!result.Success) { /* 应用宿主运维策略；本包不重试 */ }
```

`NotificationResult.Success` 表示通道上报的结果；有 provider 消息 ID 时经 `ProviderMessageId` 暴露。未知通道返回失败结果；通道异常记日志并转为通用失败结果；调用方取消正常传播。provider 异常消息与日志中不得放密钥或敏感接收者数据。

`DispatchToAllAsync` 顺序调用全部已注册通道并返回逐通道结果；`NotificationDispatchReport.Succeeded` 仅在全部成功时为 true。取消可能发生在部分通道已发送之后——非原子操作，不得当作全有或全无投递。

## 去重

去重仅在消息带 `CorrelationId` 且 `DeduplicationWindow` > 0 时生效。键为注册通道名 + correlation ID；通道查找大小写不敏感——请求大小写不产生第二个去重身份。首次发送进行中时并发重复被抑制；发送成功后窗口自完成时刻起算。失败/异常/取消会释放占用，宿主可重试。被抑制的发送返回成功且 `ProviderMessageId = "suppressed"`。

默认窗口 5 分钟、容量上限 10,000。过期项在后续符合条件的分发中回收；容量满时新的带 correlation 分发 fail closed（不改为无去重发送）。状态属单进程单例 dispatcher——不跨实例共享、不跨重启持久。持久或分布式保证请用 provider/应用幂等。

## Public API surface

_Generated from `PublicAPI.Unshipped.txt`（由 PublicApiAnalyzers 强制执行）。_

- `PubSoft.Notifications`: `DingTalkWebhookChannel` `DingTalkWebhookOptions` `INotificationChannel` `NotificationChannelExtensions` `NotificationDispatchReport` `NotificationDispatcher` `NotificationMessage` `NotificationOptions` `NotificationResult` `NotificationServiceCollectionExtensions` `NotificationSeverity` `TeamsWebhookChannel` `TeamsWebhookOptions`
