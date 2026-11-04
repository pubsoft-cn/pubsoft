using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Notifications;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class NotificationsTailWaveTests
{
    [Fact]
    public void NotificationResultRecordSemantics()
    {
        var ok = NotificationResult.Ok("pm-1");
        Assert.True(ok.Success);
        Assert.Equal("pm-1", ok.ProviderMessageId);
        var fail = NotificationResult.Fail("bad");
        Assert.False(fail.Success);
        Assert.Equal("bad", fail.Message);
        Assert.Equal(NotificationResult.Ok("x"), NotificationResult.Ok("x")); // record equality
        Assert.NotEqual(ok, fail);
    }

    [Fact]
    public void NotificationMessageCtorAndFields()
    {
        var m = new NotificationMessage(
            title: "alert", content: "cpu high",
            recipients: ["ops"], severity: NotificationSeverity.Warning,
            source: "monitor", correlationId: "c-1");
        Assert.Equal("alert", m.Title);
        Assert.Equal("cpu high", m.Content);
        Assert.Single(m.Recipients);
        Assert.Equal(NotificationSeverity.Warning, m.Severity);
        Assert.Equal("monitor", m.Source);
        Assert.Equal("c-1", m.CorrelationId);
        Assert.Empty(m.TemplateParameters);
    }

    [Fact]
    public void NotificationOptionsDefaults()
    {
        var o = new NotificationOptions();
        Assert.Equal(TimeSpan.FromMinutes(5), o.DeduplicationWindow);
        Assert.Equal(10_000, o.MaximumDeduplicationEntries);
    }

    [Fact]
    public void AddPubSoftNotificationChannelsRegistersDispatcher()
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Notifications:Teams:WebhookUrl"] = "https://t/hook",
        }).Build();
        var s = new ServiceCollection();
        s.AddPubSoftNotificationChannels(cfg.GetSection("Notifications"));
        using var sp = s.BuildServiceProvider();
        Assert.NotNull(sp.GetService<NotificationDispatcher>());
    }
}
