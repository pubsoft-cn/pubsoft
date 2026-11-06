using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace PubSoft.Messaging;

/// <summary>Routes envelopes to their registered handlers by message type.</summary>
public sealed class MessageDispatcher(JsonSerializerOptions? serializerOptions = null)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<IMessageHandlerRegistration>> _handlers = new(StringComparer.Ordinal);
    private readonly JsonSerializerOptions? _serializerOptions = serializerOptions is null
        ? null
        : new JsonSerializerOptions(serializerOptions);

    /// <summary>Registers a handler for a message type name.</summary>
    public void Register<TMessage>(string messageType, IMessageHandler<TMessage> handler)
        where TMessage : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        if (messageType.Length > 256 || messageType.Any(char.IsControl))
            throw new ArgumentException("Message types must be at most 256 non-control characters.", nameof(messageType));
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            if (!_handlers.TryGetValue(messageType, out var registrations))
            {
                registrations = [];
                _handlers.Add(messageType, registrations);
            }
            else if (registrations[0].MessageType != typeof(TMessage))
            {
                throw new InvalidOperationException(
                    $"Message type '{messageType}' is already registered for payload '{registrations[0].MessageType}'.");
            }

            registrations.Add(new MessageHandlerRegistration<TMessage>(handler, _serializerOptions));
        }
    }

    /// <summary>Deserializes the payload and invokes the matching handler; unknown types fail closed.</summary>
    public async Task DispatchAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();

        IMessageHandlerRegistration[] registrations;
        lock (_gate)
        {
            if (!_handlers.TryGetValue(envelope.MessageType, out var registeredHandlers))
                throw new InvalidOperationException($"No handler is registered for message type '{envelope.MessageType}'.");
            registrations = registeredHandlers.ToArray();
        }

        foreach (var registration in registrations)
            await registration.HandleAsync(envelope.Payload, cancellationToken).ConfigureAwait(false);
    }

    private interface IMessageHandlerRegistration
    {
        Type MessageType { get; }
        Task HandleAsync(JsonElement payload, CancellationToken cancellationToken);
    }

    private sealed class MessageHandlerRegistration<TMessage>(
        IMessageHandler<TMessage> handler,
        JsonSerializerOptions? serializerOptions) : IMessageHandlerRegistration
        where TMessage : class
    {
        public Type MessageType => typeof(TMessage);

        [UnconditionalSuppressMessage("Trimming", "IL2026",
            Justification = "Message types are registered explicitly by the host; the host-provided JsonSerializerOptions must carry a TypeInfoResolver covering them under trimming/AOT.")]
        [UnconditionalSuppressMessage("AOT", "IL3050",
            Justification = "Message types are registered explicitly by the host; the host-provided JsonSerializerOptions must carry a TypeInfoResolver covering them under trimming/AOT.")]
        public async Task HandleAsync(JsonElement payload, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var message = payload.Deserialize<TMessage>(serializerOptions)
                ?? throw new InvalidDataException($"Payload for '{typeof(TMessage).Name}' deserialized to null.");
            await handler.HandleAsync(message, cancellationToken).ConfigureAwait(false);
        }
    }
}