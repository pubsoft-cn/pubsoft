namespace PubSoft.Messaging.Outbox;


/// <summary>Serializes an outbox event payload. Implementations may use a source-generated System.Text.Json resolver for trimmed/AOT hosts.</summary>
public interface IOutboxEventSerializer
{
    /// <summary>Serializes <paramref name="event"/> using its runtime CLR type.</summary>
    string Serialize(object @event, Type runtimeType);
}
