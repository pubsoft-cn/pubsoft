using System.Text.Json;

namespace PubSoft.Messaging.Outbox;


/// <summary>System.Text.Json outbox event serializer with an explicit options/type-info resolver seam.</summary>
public sealed class OutboxEventJsonSerializer : IOutboxEventSerializer
{
    private readonly JsonSerializerOptions? _options;

    /// <summary>Creates a serializer. Trimmed/AOT hosts should pass options whose TypeInfoResolver covers all outbox event types.</summary>
    public OutboxEventJsonSerializer(JsonSerializerOptions? options = null)
    {
        _options = options;
    }

    /// <inheritdoc />
    public string Serialize(object @event, Type runtimeType)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(runtimeType);
        return JsonSerializer.Serialize(@event, runtimeType, _options);
    }
}
