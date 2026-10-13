using Microsoft.Extensions.Options;
using PubSoft.Notifications;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class NotificationsTests
{
    [Fact]
    public void NotificationMessageSnapshotsRecipientsAndTemplateParameters()
    {
        var recipients = new List<string> { " user@example.test " };
        var template = new Dictionary<string, string> { ["name"] = "Ada" };
        var message = new NotificationMessage(" hello ", recipients, templateParameters: template);
        recipients.Add("other@example.test");
        template["name"] = "changed";

        Assert.Equal("hello", message.Content);
        Assert.Equal("user@example.test", Assert.Single(message.Recipients));
        Assert.Equal("Ada", message.TemplateParameters["name"]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)message.Recipients).Add("third@example.test"));
    }

    [Fact]
    public async Task ConcurrentDuplicateDeliveryReservesOnlyOneSend()
    {
        var channel = new DelayedChannel("email");
        var dispatcher = CreateDispatcher([channel]);
        var message = CreateMessage("alert-42");

        var results = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(_ => dispatcher.DispatchAsync("EMAIL", message)));

        Assert.Single(results, result => result.Success && result.ProviderMessageId is null);
        Assert.Equal(15, results.Count(result => result.ProviderMessageId == "suppressed"));
        Assert.Equal(1, channel.SendCount);
    }

    [Fact]
    public async Task DeduplicationUsesRegisteredChannelIdentityRegardlessOfRequestedCase()
    {
        var channel = new DelayedChannel("email");
        var dispatcher = CreateDispatcher([channel]);
        var message = CreateMessage("case-insensitive-channel");

        var results = await Task.WhenAll(
            dispatcher.DispatchAsync("email", message),
            dispatcher.DispatchAsync("EMAIL", message));

        Assert.Single(results, result => result.Success && result.ProviderMessageId is null);
        Assert.Single(results, result => result.Success && result.ProviderMessageId == "suppressed");
        Assert.Equal(1, channel.SendCount);
    }

    [Fact]
    public async Task FailedSendReleasesDeduplicationReservationForRetry()
    {
        var channel = new FakeChannel("sms")
        {
            Results = new Queue<NotificationResult>([
                NotificationResult.Fail("temporary"),
                NotificationResult.Ok("provider-1")])
        };
        var dispatcher = CreateDispatcher([channel]);
        var message = CreateMessage("retryable");

        var failed = await dispatcher.DispatchAsync("sms", message);
        var retried = await dispatcher.DispatchAsync("sms", message);

        Assert.False(failed.Success);
        Assert.True(retried.Success);
        Assert.Equal(2, channel.SendCount);
    }

    [Fact]
    public async Task DeduplicationCapacityFailsClosedAndReclaimsExpiredEntries()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-09-30T00:00:00Z"));
        var channel = new FakeChannel("push");
        var dispatcher = CreateDispatcher([channel], new NotificationOptions
        {
            DeduplicationWindow = TimeSpan.FromMinutes(1),
            MaximumDeduplicationEntries = 1
        }, clock);

        Assert.True((await dispatcher.DispatchAsync("push", CreateMessage("first"))).Success);
        Assert.False((await dispatcher.DispatchAsync("push", CreateMessage("second"))).Success);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True((await dispatcher.DispatchAsync("push", CreateMessage("second"))).Success);
        Assert.Equal(2, channel.SendCount);
    }

    [Fact]
    public async Task DeduplicationWindowStartsAfterSuccessfulSend()
    {
        var clock = new MutableTimeProvider(DateTimeOffset.Parse("2026-09-30T00:00:00Z"));
        var channel = new AdvancingChannel("push", clock);
        var dispatcher = CreateDispatcher([channel], new NotificationOptions
        {
            DeduplicationWindow = TimeSpan.FromMinutes(1)
        }, clock);
        var message = CreateMessage("slow-send");

        Assert.True((await dispatcher.DispatchAsync("push", message)).Success);
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal("suppressed", (await dispatcher.DispatchAsync("push", message)).ProviderMessageId);
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True((await dispatcher.DispatchAsync("push", message)).Success);
        Assert.Equal(2, channel.SendCount);
    }

    [Fact]
    public async Task DispatchToAllReturnsPerChannelResultsAndCancellationPropagates()
    {
        var success = new FakeChannel("a");
        var failure = new FakeChannel("b") { Result = NotificationResult.Fail("offline") };
        var dispatcher = CreateDispatcher([failure, success]);

        var report = await dispatcher.DispatchToAllAsync(CreateMessage());

        Assert.False(report.Succeeded);
        Assert.Equal(2, report.Results.Count);
        Assert.True(report.Results["a"].Success);
        Assert.False(report.Results["b"].Success);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            dispatcher.DispatchToAllAsync(CreateMessage(), new CancellationToken(canceled: true)));
    }

    [Fact]
    public void ConstructorRejectsDuplicateChannelNamesAndInvalidOptions()
    {
        Assert.Throws<ArgumentException>(() => CreateDispatcher([
            new FakeChannel("email"), new FakeChannel("EMAIL") ]));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateDispatcher([new FakeChannel("email")], new NotificationOptions
        {
            MaximumDeduplicationEntries = 0
        }));
    }

    private static NotificationDispatcher CreateDispatcher(
        IEnumerable<INotificationChannel> channels,
        NotificationOptions? options = null,
        TimeProvider? clock = null)
        => new(channels, Options.Create(options ?? new NotificationOptions()), timeProvider: clock);

    private static NotificationMessage CreateMessage(string? correlationId = null)
        => new("test notification", source: "tests", correlationId: correlationId);

    private class FakeChannel(string name) : INotificationChannel
    {
        public string Name { get; } = name;
        public int SendCount;
        public NotificationResult Result { get; set; } = NotificationResult.Ok();
        public Queue<NotificationResult>? Results { get; set; }

        public virtual Task<NotificationResult> SendAsync(
            NotificationMessage message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref SendCount);
            return Task.FromResult(Results is { Count: > 0 } ? Results.Dequeue() : Result);
        }
    }

    private sealed class DelayedChannel(string name) : FakeChannel(name)
    {
        public override async Task<NotificationResult> SendAsync(
            NotificationMessage message,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref SendCount);
            await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken);
            return Result;
        }
    }

    private sealed class AdvancingChannel(string name, MutableTimeProvider clock) : FakeChannel(name)
    {
        public override Task<NotificationResult> SendAsync(
            NotificationMessage message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref SendCount);
            clock.Advance(TimeSpan.FromMinutes(2));
            return Task.FromResult(Result);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
}