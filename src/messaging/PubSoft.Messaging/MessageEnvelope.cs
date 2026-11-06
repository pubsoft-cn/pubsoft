using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace PubSoft.Messaging;

/// <summary>Transport-neutral message wrapper: id, type, payload, provenance.</summary>
public sealed class MessageEnvelope
{
    /// <summary>Creates an envelope; <paramref name="messageId"/> and <paramref name="messageType"/> are required.</summary>
    public MessageEnvelope(
        string messageId,
        string messageType,
        JsonElement payload,
        DateTimeOffset occurredAt,
        string? source = null,
        string? target = null)
    {
        MessageId = ValidateIdentifier(messageId, nameof(messageId), 128);
        MessageType = ValidateIdentifier(messageType, nameof(messageType), 256);
        if (payload.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException("Payload must be a defined JSON value.", nameof(payload));

        Payload = payload.Clone();
        OccurredAt = occurredAt;
        Source = ValidateOptionalLabel(source, nameof(source));
        Target = ValidateOptionalLabel(target, nameof(target));
    }

    /// <summary>Unique message id (dedupe key).</summary>
    public string MessageId { get; }
    /// <summary>Type discriminator used by the dispatcher.</summary>
    public string MessageType { get; }
    /// <summary>The serialized payload.</summary>
    public JsonElement Payload { get; }
    /// <summary>When the event occurred.</summary>
    public DateTimeOffset OccurredAt { get; }
    /// <summary>Publishing service tag.</summary>
    public string? Source { get; }
    /// <summary>Intended consumer tag.</summary>
    public string? Target { get; }

    /// <summary>Deserializes the payload into <typeparamref name="T"/> using the dispatcher's serializer options.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Payloads are serialized with host-provided JsonSerializerOptions; trimmed/AOT hosts must supply a TypeInfoResolver covering payload types.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Payloads are serialized with host-provided JsonSerializerOptions; trimmed/AOT hosts must supply a TypeInfoResolver covering payload types.")]
    public static MessageEnvelope Create<TPayload>(
        string messageId,
        string messageType,
        TPayload payload,
        DateTimeOffset occurredAt,
        string? source = null,
        string? target = null,
        JsonSerializerOptions? serializerOptions = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var jsonPayload = JsonSerializer.SerializeToElement(payload, serializerOptions);
        return new MessageEnvelope(messageId, messageType, jsonPayload, occurredAt, source, target);
    }

    /// <summary>Deserializes the payload into <typeparamref name="TPayload"/> using the dispatcher's serializer options.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "Payloads are deserialized with host-provided JsonSerializerOptions; trimmed/AOT hosts must supply a TypeInfoResolver covering payload types.")]
    [UnconditionalSuppressMessage("AOT", "IL3050",
        Justification = "Payloads are deserialized with host-provided JsonSerializerOptions; trimmed/AOT hosts must supply a TypeInfoResolver covering payload types.")]
    public TPayload? DeserializePayload<TPayload>(JsonSerializerOptions? serializerOptions = null)
        => Payload.Deserialize<TPayload>(serializerOptions);

    private static string ValidateIdentifier(string value, string parameterName, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > maxLength || value.Any(char.IsControl))
            throw new ArgumentException($"Value must be at most {maxLength} non-control characters.", parameterName);
        return value;
    }

    private static string? ValidateOptionalLabel(string? value, string parameterName)
    {
        if (value is null)
            return null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl))
            throw new ArgumentException("Optional labels must be non-empty, at most 256 non-control characters.", parameterName);
        return value;
    }
}