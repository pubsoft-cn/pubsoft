---
purpose: package-level hosting guide for PubSoft.Notifications
audience: integrating host developers
owner: PubSoft maintainers
last-reviewed: 2026-10-13
---

# PubSoft.Notifications

**English** | [简体中文](https://github.com/pubsoft-cn/pubsoft/blob/main/src/platform/PubSoft.Notifications/README.zh-CN.md)


`PubSoft.Notifications` provides immutable notification messages, channel contracts, dispatch results, bounded in-process deduplication, and two built-in webhook channels (`teams`, `dingtalk`). It does not include persistent delivery or cross-instance deduplication. Vendor-specific integrations (SMS providers, email, push) intentionally live in the host application as its own `INotificationChannel` implementations.

## Registration

Register each channel and configure the dispatcher before resolving it:

```csharp
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Notifications;

// Built-in channels bound to a configuration section (subsections Teams / DingTalk;
// a channel is registered only when its section exists):
services.AddPubSoftNotificationChannels(configuration.GetSection("Notifications"));

// Or register channels and configure the dispatcher individually:
services.AddTeamsNotificationChannel(configuration.GetSection("Notifications:Teams"));
services.AddDingTalkNotificationChannel(options => { options.WebhookUrl = webhookUrl; });
// Custom channel:
services.AddSingleton<INotificationChannel, EmailNotificationChannel>();
services.AddPubSoftNotifications(options =>
{
    options.DeduplicationWindow = TimeSpan.FromMinutes(5);
    options.MaximumDeduplicationEntries = 10_000;
});
```

Built-in channel HTTP calls go through named `IHttpClientFactory` clients with `PubSoft.Resilience.Http` policy defaults (total timeout 10s, per-attempt 5s, one retry). Webhook URLs must be absolute `https`; non-loopback `http` fails options validation, loopback stays exempt for local development, and per-channel `AllowInsecureUrl` covers deliberate exceptions.

`AddPubSoftNotifications` registers `NotificationDispatcher` as a singleton. Its channel instances are resolved once and retained, so channel implementations and their dependencies must be safe for singleton use. For HTTP integrations, prefer `IHttpClientFactory`; do not inject scoped services into a channel. Channel names must be unique (case-insensitively) and non-empty.

## Dispatch

Create a message with the information required by the selected provider. Message content, recipients, and template parameters are copied into immutable snapshots:

```csharp
var message = new NotificationMessage(
    content: "Your report is ready.",
    recipients: ["user@example.test"],
    title: "Report complete",
    templateCode: "report-ready-v1",
    templateParameters: new Dictionary<string, string>
    {
        ["reportId"] = reportId
    },
    source: "reporting",
    correlationId: $"report-ready:{reportId}");

var result = await dispatcher.DispatchAsync("email", message, cancellationToken);
if (!result.Success)
{
    // Apply the host's operational policy; this package does not retry.
}
```

`NotificationResult.Success` indicates the channel's reported outcome. A provider message ID, when available, is exposed as `ProviderMessageId`. An unknown channel returns a failed result. A channel exception is logged and converted to a generic failed result; caller cancellation is propagated. Do not put secrets or sensitive recipient data in provider exception messages or logs.

`DispatchToAllAsync` invokes every registered channel sequentially and returns a result for each channel. `NotificationDispatchReport.Succeeded` is true only when all results succeed. Cancellation can occur after earlier channels have already sent, so this operation is not atomic and must not be treated as all-or-nothing delivery.

## Deduplication

Deduplication applies only when the message has a `CorrelationId` and `DeduplicationWindow` is greater than zero. The key uses the registered channel name plus correlation ID; channel lookup is case-insensitive, so request casing does not create another deduplication identity. Concurrent duplicates are suppressed while the first send is in progress; after a successful send, the window begins at completion. Failed sends, exceptions, and cancellation release the reservation so the host can retry. A suppressed send returns success with `ProviderMessageId` set to `"suppressed"`.

The default window is five minutes and the default capacity is 10,000 entries. Expired entries are reclaimed during later eligible dispatches (a non-empty correlation ID while deduplication is enabled). When capacity is full, a new correlated dispatch fails closed rather than sending without deduplication. This state belongs to the singleton dispatcher in one process; it is not shared across instances or persisted across restarts. Use provider/application idempotency for durable or distributed guarantees.

## Public API surface

_Generated from `PublicAPI.Unshipped.txt` (enforced by PublicApiAnalyzers)._

- `PubSoft.Notifications`: `DingTalkWebhookChannel` `DingTalkWebhookOptions` `INotificationChannel` `NotificationChannelExtensions` `NotificationDispatchReport` `NotificationDispatcher` `NotificationMessage` `NotificationOptions` `NotificationResult` `NotificationServiceCollectionExtensions` `NotificationSeverity` `TeamsWebhookChannel` `TeamsWebhookOptions`

## Verification

```bash
dotnet test tests/PubSoft.Core.Tests/PubSoft.Core.Tests.csproj -c Release --filter FullyQualifiedName~NotificationsTests
bash scripts/verify-package-consumer.sh
```