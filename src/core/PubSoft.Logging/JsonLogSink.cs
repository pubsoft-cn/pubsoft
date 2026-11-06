using System.Buffers;
using System.Text.Json;

namespace PubSoft.Logging;

/// <summary>JSON-lines file sink.</summary>
public sealed class JsonLogSink(
    Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> output) : ILogSink
{
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> _output =
        output ?? throw new ArgumentNullException(nameof(output));

    /// <inheritdoc />
    public async ValueTask WriteBatchAsync(ReadOnlyMemory<LogEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.IsEmpty)
            return;

        var buffer = new ArrayBufferWriter<byte>(Math.Max(256, entries.Length * 256));
        for (var index = 0; index < entries.Length; index++)
        {
            WriteEntry(buffer, entries.Span[index]);
            buffer.GetSpan(1)[0] = (byte)'\n';
            buffer.Advance(1);
        }

        await _output(buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
    }

    private static void WriteEntry(IBufferWriter<byte> buffer, LogEntry entry)
    {
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteString("timestamp", entry.TimestampUtc);
        writer.WriteString("level", (entry.Level switch { AppLogLevel.Trace => "Trace", AppLogLevel.Debug => "Debug", AppLogLevel.Warn => "Warn", AppLogLevel.Error => "Error", AppLogLevel.Fatal => "Fatal", _ => "Info" }).ToUpperInvariant());
        writer.WriteString("message", entry.Message);
        writer.WriteString("source", entry.Source);
        if (entry.TraceId != 0)
            writer.WriteNumber("trace_id", entry.TraceId);
        WriteOptionalString(writer, "user_id", entry.UserId);
        WriteOptionalString(writer, "service_name", entry.Service);
        WriteOptionalString(writer, "service_version", entry.ServiceVersion);
        WriteOptionalString(writer, "service_instance_id", entry.ServiceInstanceId);
        WriteOptionalString(writer, "span_id", entry.SpanId);
        WriteOptionalString(writer, "env", entry.Environment);
        WriteOptionalString(writer, "host_name", entry.HostName);
        if (entry.Context is { Count: > 0 } context)
        {
            writer.WriteStartObject("context");
            foreach (var pair in context)
                WriteContextValue(writer, pair.Key, pair.Value);
            writer.WriteEndObject();
        }
        if (entry.ExceptionDetail is not null)
            writer.WriteString("exception", entry.ExceptionDetail);
        writer.WriteEndObject();
        writer.Flush();
    }

    private static void WriteOptionalString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
            writer.WriteString(name, value);
    }

    private static void WriteContextValue(Utf8JsonWriter writer, string key, object? value)
    {
        switch (value)
        {
            case null: writer.WriteNull(key); break;
            case string text: writer.WriteString(key, text); break;
            case bool boolean: writer.WriteBoolean(key, boolean); break;
            case byte number: writer.WriteNumber(key, number); break;
            case short number: writer.WriteNumber(key, number); break;
            case int number: writer.WriteNumber(key, number); break;
            case long number: writer.WriteNumber(key, number); break;
            case float number when float.IsFinite(number): writer.WriteNumber(key, number); break;
            case double number when double.IsFinite(number): writer.WriteNumber(key, number); break;
            case decimal number: writer.WriteNumber(key, number); break;
            case Guid guid: writer.WriteString(key, guid); break;
            case DateTime dateTime: writer.WriteString(key, dateTime); break;
            case DateTimeOffset dateTimeOffset: writer.WriteString(key, dateTimeOffset); break;
            case Enum enumValue: writer.WriteString(key, enumValue.ToString()); break;
            default: writer.WriteString(key, value.ToString()); break;
        }
    }
}