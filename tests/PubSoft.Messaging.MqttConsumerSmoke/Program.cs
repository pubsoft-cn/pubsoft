using Microsoft.Extensions.DependencyInjection;
using PubSoft.Messaging.Mqtt;
using PubSoft.Messaging.Outbox;

// Options defaults align with broker contract: QoS1, persistent session, 64KiB cap.
var options = new MqttOutboxOptions();
if (options.QualityOfService != 1
    || !options.PersistentSession
    || options.MaxPayloadBytes != 64 * 1024
    || options.PublishTimeout != TimeSpan.FromSeconds(10))
    throw new InvalidOperationException("MqttOutboxOptions defaults changed.");

// MQTT5 session-expiry pairing: persistent sessions must carry an explicit expiry.
var inbox = new MqttInboxOptions();
if (!inbox.PersistentSession || inbox.SessionExpirySeconds != 86_400)
    throw new InvalidOperationException("MqttInboxOptions session-expiry pairing changed.");

// Option guards fire through the dispatcher ctor (CloneAndValidate is internal).
await using var _ = new MqttOutboxDispatcher(new MqttOutboxOptions());
try { await using var d = new MqttOutboxDispatcher(new MqttOutboxOptions { BrokerUri = " " }); throw new InvalidOperationException("bad uri accepted"); }
catch (ArgumentException) { }
try { await using var d = new MqttOutboxDispatcher(new MqttOutboxOptions { QualityOfService = 3 }); throw new InvalidOperationException("bad qos accepted"); }
catch (ArgumentOutOfRangeException) { }
try { await using var d = new MqttOutboxDispatcher(new MqttOutboxOptions { MaxPayloadBytes = 0 }); throw new InvalidOperationException("bad cap accepted"); }
catch (ArgumentOutOfRangeException) { }

// DI wires the dispatcher as the outbox transport.
var services = new ServiceCollection();
services.AddPubSoftMqttOutboxDispatcher(o => o.BrokerUri = "mqtt://example:1883");
await using var provider = services.BuildServiceProvider();
if (provider.GetRequiredService<IOutboxDispatcher>() is not MqttOutboxDispatcher)
    throw new InvalidOperationException("IOutboxDispatcher not wired to MqttOutboxDispatcher.");

// Dispatch rejects a null message before touching the network.
var dispatcher = provider.GetRequiredService<IOutboxDispatcher>();
try { await dispatcher.DispatchAsync(null!); throw new InvalidOperationException("null message accepted"); }
catch (ArgumentNullException) { }

// Outbox identity travels as MQTT5 user properties.
if (MqttInboxHeaders.OutboxMessageId != "x-outbox-id" || MqttInboxHeaders.OutboxAggregateKey != "x-outbox-aggkey")
    throw new InvalidOperationException("Outbox identity header names changed.");

Console.WriteLine("MqttConsumerSmoke OK");
