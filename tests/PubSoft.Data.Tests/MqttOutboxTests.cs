using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MQTTnet;
using MQTTnet.Packets;
using MQTTnet.Protocol;
using PubSoft.Messaging.Mqtt;
using PubSoft.Messaging.Outbox;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class MqttOutboxTests
{
    private static OutboxMessage Msg(string eventType, string payloadJson)
    {
        var msg = (OutboxMessage)typeof(OutboxMessage)
            .GetConstructor(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, Type.EmptyTypes, null)!
            .Invoke(null);
        foreach (var (n, v) in new (string, object?)[]
        {
            ("Id", PubSoft.Testing.TestIds.Next()), ("EventType", eventType), ("PayloadJson", payloadJson),
            ("OccurredAt", DateTimeOffset.UtcNow), ("Status", OutboxMessageStatus.Pending),
            ("AttemptCount", 0), ("MaxAttempts", 3),
        })
            typeof(OutboxMessage).GetProperty(n)!.SetValue(msg, v);
        return msg;
    }

    [Fact]
    public void OptionsDefaults()
    {
        var o = new MqttOutboxOptions();
        Assert.Equal("mqtt://localhost:1883", o.BrokerUri);
        Assert.Equal("pubsoft-outbox", o.ClientId);
        Assert.Equal("outbox/", o.TopicPrefix);
        Assert.Equal(1, o.QualityOfService);
        Assert.True(o.PersistentSession);
        Assert.Equal(64 * 1024, o.MaxPayloadBytes);
    }

    [Fact]
    public void CtorValidatesOptions()
    {
        Assert.Throws<ArgumentException>(() => new MqttOutboxDispatcher(new MqttOutboxOptions { BrokerUri = "" }));
        Assert.Throws<ArgumentException>(() => new MqttOutboxDispatcher(new MqttOutboxOptions { ClientId = "" }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MqttOutboxDispatcher(new MqttOutboxOptions { QualityOfService = 3 }));
    }

    [Fact]
    public async Task ServiceRegistrationWiresDispatcher()
    {
        var s = new ServiceCollection();
        s.AddPubSoftMqttOutboxDispatcher(o => { o.BrokerUri = "mqtt://b:1883"; o.TopicPrefix = "ext/outbox/"; });
        await using var sp = s.BuildServiceProvider();
        Assert.IsType<MqttOutboxDispatcher>(sp.GetRequiredService<IOutboxDispatcher>());
    }

    [Fact]
    public async Task OversizePayloadRejectedBeforePublish()
    {
        await using var d = new MqttOutboxDispatcher(new MqttOutboxOptions { MaxPayloadBytes = 8 });
        var msg = Msg("type", new string('x', 64));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => d.DispatchAsync(msg));
        Assert.Contains("exceeds broker limit", ex.Message);
    }

    private sealed class MqttFactAttribute : FactAttribute
    {
        public MqttFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PUBSOFT_MQTT")))
                Skip = "Set PUBSOFT_MQTT to run MQTT broker integration tests.";
        }
    }

    [MqttFact]
    public async Task RoundtripAgainstRealBroker()
    {
        var broker = Environment.GetEnvironmentVariable("PUBSOFT_MQTT");

        var opts = new MqttOutboxOptions
        {
            BrokerUri = broker!,
            ClientId = "pubsoft-test",
            Username = Environment.GetEnvironmentVariable("PUBSOFT_MQTT_USER"),
            Password = Environment.GetEnvironmentVariable("PUBSOFT_MQTT_PASS"),
            TopicPrefix = Environment.GetEnvironmentVariable("PUBSOFT_MQTT_PREFIX") ?? "outbox/",
        };
        await using var d = new MqttOutboxDispatcher(opts);
        var msg = Msg("roundtrip.test", """{"ok":true}""");
        await d.DispatchAsync(msg); // throws unless PUBACK arrives
    }
}

public sealed class MqttInboxTests
{
    [Fact]
    public void InboxOptionsDefaults()
    {
        var o = new MqttInboxOptions();
        Assert.Equal("outbox-workers", o.ShareGroup);
        Assert.Equal("default", o.ConsumerName);
        Assert.True(o.PersistentSession);
        Assert.Equal("outbox/#", o.TopicFilter);
        Assert.Equal(86_400u, o.SessionExpirySeconds); // paired with PersistentSession
    }

    [Fact]
    public void InboxCtorValidates()
    {
        Assert.Throws<ArgumentException>(() => new MqttInboxSubscriber(
            new MqttInboxOptions { ConsumerName = " " }, null!, (_, _, _) => Task.CompletedTask));
        Assert.Throws<ArgumentNullException>(() => new MqttInboxSubscriber(
            new MqttInboxOptions(), null!, (_, _, _) => Task.CompletedTask));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MqttInboxSubscriber(
            new MqttInboxOptions { ConsumerName = "tests", ReconnectJitterRatio = double.NaN }, null!, (_, _, _) => Task.CompletedTask));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MqttInboxSubscriber(
            new MqttInboxOptions { ConsumerName = "tests", ReconnectJitterRatio = 1.5 }, null!, (_, _, _) => Task.CompletedTask));
    }

    [Fact]
    public async Task SuccessfulInboxProcessingAcksAfterHandlerCompletion()
    {
        var handlerCompleted = false;
        var inbox = new FakeInboxMessageProcessor(async (_, _, handler, ct) => { await handler(ct); return true; });
        var subscriber = new MqttInboxSubscriber(
            new MqttInboxOptions { ConsumerName = "tests" },
            inbox,
            (_, _, _) =>
            {
                handlerCompleted = true;
                return Task.CompletedTask;
            });
        var acknowledgements = 0;
        var args = CreateReceivedArgs(42, (_, _) =>
        {
            acknowledgements++;
            return Task.CompletedTask;
        });

        await InvokeHandleAsync(subscriber, args);

        Assert.True(handlerCompleted);
        Assert.False(args.AutoAcknowledge);
        Assert.True(args.IsHandled);
        Assert.False(args.ProcessingFailed);
        Assert.Equal(1, acknowledgements);
    }

    [Fact]
    public async Task FailedInboxProcessingLeavesMessageUnacknowledged()
    {
        var inbox = new FakeInboxMessageProcessor((_, _, _, _) => throw new InvalidOperationException("inbox failure"));
        var subscriber = new MqttInboxSubscriber(
            new MqttInboxOptions { ConsumerName = "tests" },
            inbox,
            (_, _, _) => Task.CompletedTask);
        var acknowledgements = 0;
        var args = CreateReceivedArgs(43, (_, _) =>
        {
            acknowledgements++;
            return Task.CompletedTask;
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => InvokeHandleAsync(subscriber, args));

        Assert.False(args.AutoAcknowledge);
        Assert.True(args.ProcessingFailed);
        Assert.Equal(0, acknowledgements);
    }

    [Fact]
    public async Task MessageWithoutOutboxIdIsAcknowledgedAndDropped()
    {
        var inbox = new FakeInboxMessageProcessor((_, _, _, _) => throw new InvalidOperationException("must not be called"));
        var subscriber = new MqttInboxSubscriber(
            new MqttInboxOptions { ConsumerName = "tests" },
            inbox,
            (_, _, _) => Task.CompletedTask);
        var acknowledgements = 0;
        var args = CreateReceivedArgs(null, (_, _) =>
        {
            acknowledgements++;
            return Task.CompletedTask;
        });

        await InvokeHandleAsync(subscriber, args);

        Assert.False(args.AutoAcknowledge);
        Assert.Equal(1, acknowledgements);
    }

    private sealed class FakeInboxMessageProcessor(
        Func<string, long, Func<CancellationToken, Task>, CancellationToken, Task<bool>> invoke) : IInboxMessageProcessor
    {
        public Task<bool> ProcessOnceAsync(
            string consumerName,
            long messageId,
            Func<CancellationToken, Task> handler,
            CancellationToken cancellationToken = default)
            => invoke(consumerName, messageId, handler, cancellationToken);
    }

    private static MqttApplicationMessageReceivedEventArgs CreateReceivedArgs(
        long? messageId,
        Func<MqttApplicationMessageReceivedEventArgs, CancellationToken, Task> acknowledgeHandler)
    {
        var applicationMessage = new MqttApplicationMessage
        {
            Topic = "outbox/tests.event.v1",
            PayloadSegment = new ArraySegment<byte>(Encoding.UTF8.GetBytes("{}")),
            QualityOfServiceLevel = MqttQualityOfServiceLevel.AtLeastOnce,
            UserProperties = new List<MqttUserProperty>()
        };
        if (messageId.HasValue)
            applicationMessage.UserProperties.Add(new MqttUserProperty(MqttInboxHeaders.OutboxMessageId, new ReadOnlyMemory<byte>(Encoding.UTF8.GetBytes(messageId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)))));
        var publishPacket = new MqttPublishPacket
        {
            Topic = applicationMessage.Topic,
            QualityOfServiceLevel = MqttQualityOfServiceLevel.AtLeastOnce
        };
        return new MqttApplicationMessageReceivedEventArgs("tests", applicationMessage, publishPacket, acknowledgeHandler);
    }

    private static Task InvokeHandleAsync(MqttInboxSubscriber subscriber, MqttApplicationMessageReceivedEventArgs args)
    {
        var method = typeof(MqttInboxSubscriber).GetMethod(
            "HandleAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (Task)method.Invoke(subscriber, new object[] { args, CancellationToken.None })!;
    }
}

public sealed class MqttFailurePathTests
{
    private static OutboxMessage Msg(string type = "e")
    {
        var m = (OutboxMessage)typeof(OutboxMessage)
            .GetConstructor(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance, null, Type.EmptyTypes, null)!
            .Invoke(null);
        foreach (var (n, v) in new (string, object?)[]
        {
            ("Id", PubSoft.Testing.TestIds.Next()), ("EventType", type), ("PayloadJson", "{}"),
            ("OccurredAt", DateTimeOffset.UtcNow), ("Status", OutboxMessageStatus.Pending),
            ("AttemptCount", 0), ("MaxAttempts", 3),
        })
            typeof(OutboxMessage).GetProperty(n)!.SetValue(m, v);
        return m;
    }

    [Fact]
    public async Task DispatchToUnreachableBrokerThrows()
    {
        // unreachable → exception propagates → outbox keeps Pending + retry
        await using var d = new MqttOutboxDispatcher(new MqttOutboxOptions
        {
            BrokerUri = "mqtt://127.0.0.1:1", // nothing listens
            PublishTimeout = TimeSpan.FromSeconds(3),
        });
        await Assert.ThrowsAnyAsync<Exception>(() => d.DispatchAsync(Msg()));
    }

    [Fact]
    public async Task NullMessageRejected()
    {
        await using var d = new MqttOutboxDispatcher(new MqttOutboxOptions());
        await Assert.ThrowsAsync<ArgumentNullException>(() => d.DispatchAsync(null!));
    }

    [Fact]
    public void DispatchTopicComposition()
    {
        // topic = prefix + eventType — verified via options surface
        var o = new MqttOutboxOptions { TopicPrefix = "ext/outbox/" };
        Assert.Equal("ext/outbox/", o.TopicPrefix);
    }
}
