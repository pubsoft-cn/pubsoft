using Microsoft.Extensions.DependencyInjection;
using PubSoft.Notifications;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class NotificationsTailWave2Tests
{
    private sealed class F : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    [Fact]
    public void ChannelNameConstants()
    {
        var f = new F();
        var t = new TeamsWebhookChannel(f, Microsoft.Extensions.Options.Options.Create(new TeamsWebhookOptions { WebhookUrl = "https://t/h" }));
        Assert.Equal("teams", t.Name);
        var d = new DingTalkWebhookChannel(f, Microsoft.Extensions.Options.Options.Create(new DingTalkWebhookOptions { WebhookUrl = "https://d/h" }));
        Assert.Equal("dingtalk", d.Name);
    }

    [Fact]
    public void DispatchReportRecord()
    {
        var r = new NotificationDispatchReport(new Dictionary<string, NotificationResult>
        { ["teams"] = NotificationResult.Ok() });
        Assert.True(r.Succeeded);
        var r2 = new NotificationDispatchReport(new Dictionary<string, NotificationResult>
        { ["teams"] = NotificationResult.Fail("x") });
        Assert.False(r2.Succeeded);
    }

    [Fact]
    public void AddPubSoftNotificationsRegisters()
    {
        var s = new ServiceCollection();
        s.AddPubSoftNotifications(o => o.DeduplicationWindow = TimeSpan.FromMinutes(1));
        Assert.NotEmpty(s);
    }
}
