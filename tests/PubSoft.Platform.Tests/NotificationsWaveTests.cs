using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PubSoft.Notifications;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class NotificationsWaveTests
{
    private sealed class FakeChannel(string name) : INotificationChannel
    {
        public string Name => name;
        public int Calls;
        public Task<NotificationResult> SendAsync(NotificationMessage message, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(NotificationResult.Ok());
        }
    }

    private static NotificationMessage Msg(string? correlation = null)
        => new("content", correlationId: correlation);

    [Fact]
    public async Task DispatchUnknownChannelFails()
    {
        var d = new NotificationDispatcher([]);
        var r = await d.DispatchAsync("nope", Msg());
        Assert.False(r.Success);
        Assert.Contains("not registered", r.Message);
    }

    [Fact]
    public async Task DispatchRegisteredChannelSucceeds()
    {
        var ch = new FakeChannel("ops");
        var d = new NotificationDispatcher([ch]);
        var r = await d.DispatchAsync("ops", Msg());
        Assert.True(r.Success);
        Assert.Equal(1, ch.Calls);
    }

    [Fact]
    public async Task DeduplicationSuppressesSameCorrelationWithinWindow()
    {
        var ch = new FakeChannel("ops");
        var options = Options.Create(new NotificationOptions { DeduplicationWindow = TimeSpan.FromMinutes(5) });
        var d = new NotificationDispatcher([ch], options);
        await d.DispatchAsync("ops", Msg("corr-1"));
        await d.DispatchAsync("ops", Msg("corr-1")); // duplicate → suppressed
        Assert.Equal(1, ch.Calls);
        await d.DispatchAsync("ops", Msg("corr-2")); // new correlation → sent
        Assert.Equal(2, ch.Calls);
    }

    [Fact]
    public void DispatcherRejectsDuplicateChannelNames()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new NotificationDispatcher([new FakeChannel("a"), new FakeChannel("A")]));
        Assert.Contains("already registered", ex.Message);
    }

    private sealed class StubFactory(string body, HttpStatusCode status = HttpStatusCode.OK) : IHttpClientFactory
    {
        public HttpRequestMessage? LastRequest;
        public HttpClient CreateClient(string name)
        {
            var handler = new Capture(this);
            return new HttpClient(handler);
        }
        private sealed class Capture(StubFactory outer) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                outer.LastRequest = r;
                return Task.FromResult(new HttpResponseMessage(outer.Status)
                { Content = new StringContent(outer.Body, Encoding.UTF8, "application/json") });
            }
        }
        public string Body { get; } = body;
        public HttpStatusCode Status { get; } = status;
    }

    [Fact]
    public async Task DingTalkChannelSendsWebhookAndParsesErrcode()
    {
        var factory = new StubFactory("{\"errcode\":0,\"errmsg\":\"ok\"}");
        var channel = new DingTalkWebhookChannel(factory,
            Options.Create(new DingTalkWebhookOptions { WebhookUrl = "https://oapi.dingtalk.com/robot/send?access_token=x", Secret = "sec" }),
            NullLogger<DingTalkWebhookChannel>.Instance);
        var r = await channel.SendAsync(Msg());
        Assert.True(r.Success);
        Assert.NotNull(factory.LastRequest);
    }

    [Fact]
    public async Task DingTalkChannelFailsOnErrcode()
    {
        var factory = new StubFactory("{\"errcode\":310000,\"errmsg\":\"sign error\"}");
        var channel = new DingTalkWebhookChannel(factory,
            Options.Create(new DingTalkWebhookOptions { WebhookUrl = "https://oapi.dingtalk.com/robot/send?access_token=x" }),
            NullLogger<DingTalkWebhookChannel>.Instance);
        var r = await channel.SendAsync(Msg());
        Assert.False(r.Success);
    }

    [Fact]
    public void NotificationResultFactories()
    {
        Assert.True(NotificationResult.Ok("id1").Success);
        Assert.False(NotificationResult.Fail("e").Success);
        Assert.Equal("id1", NotificationResult.Ok("id1").ProviderMessageId);
        Assert.Equal("e", NotificationResult.Fail("e").Message);
    }
}

public sealed class TeamsWaveTests
{
    private sealed class StubFactory2(string body, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK) : IHttpClientFactory
    {
        public string? LastBody;
        public HttpClient CreateClient(string name)
            => new(new Capture(this));
        private sealed class Capture(StubFactory2 outer) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                outer.LastBody = r.Content is null ? null : await r.Content.ReadAsStringAsync(ct);
                return new HttpResponseMessage(outer.Status)
                { Content = new StringContent(outer.Body) };
            }
        }
        public string Body { get; } = body;
        public System.Net.HttpStatusCode Status { get; } = status;
    }

    private static PubSoft.Notifications.NotificationMessage NMsg(PubSoft.Notifications.NotificationSeverity sev)
        => new("content", title: "t", severity: sev);

    [Fact]
    public async Task TeamsChannelFailsWithoutWebhookUrl()
    {
        var channel = new PubSoft.Notifications.TeamsWebhookChannel(new StubFactory2("{}"),
            Microsoft.Extensions.Options.Options.Create(new PubSoft.Notifications.TeamsWebhookOptions()));
        var r = await channel.SendAsync(NMsg(PubSoft.Notifications.NotificationSeverity.Info));
        Assert.False(r.Success);
        Assert.Contains("not configured", r.Message);
    }

    [Fact]
    public async Task TeamsChannelPostsMessageCard()
    {
        var factory = new StubFactory2("1");
        var channel = new PubSoft.Notifications.TeamsWebhookChannel(factory,
            Microsoft.Extensions.Options.Options.Create(new PubSoft.Notifications.TeamsWebhookOptions
            { WebhookUrl = "https://t.test/webhook" }));
        var r = await channel.SendAsync(NMsg(PubSoft.Notifications.NotificationSeverity.Critical));
        Assert.True(r.Success);
        Assert.Contains("MessageCard", factory.LastBody);
        Assert.Contains("FF0000", factory.LastBody); // critical → red theme
    }

    [Theory]
    [InlineData("dingtalk")]
    [InlineData("teams")]
    public void WebhookChannelRejectsPlaintextHttpForRemoteHost(string channel)
    {
        var services = new ServiceCollection();
        if (channel == "dingtalk")
            services.AddDingTalkNotificationChannel(o => o.WebhookUrl = "http://bot.example.com/robot/send");
        else
            services.AddTeamsNotificationChannel(o => o.WebhookUrl = "http://bot.example.com/webhook");
        using var provider = services.BuildServiceProvider();

        if (channel == "dingtalk")
            Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IOptions<DingTalkWebhookOptions>>().Value);
        else
            Assert.Throws<OptionsValidationException>(
                () => provider.GetRequiredService<IOptions<TeamsWebhookOptions>>().Value);
    }

    [Fact]
    public void WebhookChannelAllowsLoopbackHttpAndExplicitOptIn()
    {
        var services = new ServiceCollection();
        services.AddTeamsNotificationChannel(o => o.WebhookUrl = "http://localhost:5000/hook");
        services.AddDingTalkNotificationChannel(o =>
        {
            o.WebhookUrl = "http://bot.example.com/robot/send";
            o.AllowInsecureUrl = true;
        });
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IOptions<TeamsWebhookOptions>>().Value);
        Assert.NotNull(provider.GetRequiredService<IOptions<DingTalkWebhookOptions>>().Value);
    }
}
