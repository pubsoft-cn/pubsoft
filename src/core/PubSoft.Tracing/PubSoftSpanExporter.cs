using System.Buffers;
using System.Diagnostics;
using System.Text.Json;
using PubSoft.Logging;

namespace PubSoft.Tracing;

/// <summary>
/// Listens to every configured <see cref="ActivitySource"/> and writes one jsonl span
/// document per stopped W3C activity: <c>{ts, durMs, tid, sid, psid, name, kind,
/// status, err?, svc, svc_ver?, tags?}</c>. The <c>tid</c>/<c>sid</c>/<c>psid</c>
/// fields are the join keys shared with audit rows and CDC events; <c>tid:sid</c>
/// already identifies a span uniquely - hosts needing a dedup-ingest document id
/// or a doc-type discriminator shape their own docs via <c>Output</c>.
/// <para>
/// Span production is bounded: when the queue is full the span is dropped and
/// <see cref="DroppedCount"/> increments - tracing never slows the request path.
/// Dispose drains pending spans before stopping.
/// </para>
/// </summary>
public sealed class PubSoftSpanExporter : IAsyncDisposable
{
    private readonly PubSoftTracingOptions _options;
    private readonly Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? _output;
    private readonly Func<ReadOnlyMemory<SpanDoc>, CancellationToken, ValueTask>? _docOutput;
    private readonly ActivityListener _listener;
    private readonly Backbone _backbone;
    private long _dropped;

    /// <summary>Validates options, opens the output, and starts listening immediately.</summary>
    public PubSoftSpanExporter(PubSoftTracingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _docOutput = options.DocOutput;
        _output = _docOutput is null
            ? options.Output ?? TracingFileOutput.ForDirectory(options.Directory!, options.ServiceName!)
            : null;
        _backbone = new Backbone(options.Capacity, options.BatchSize, WriteSpansAsync);
        _listener = new ActivityListener
        {
            ShouldListenTo = options.ShouldListenTo ?? (_ => true),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => options.Sampling,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => options.Sampling,
            ActivityStopped = OnStopped
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>Spans dropped because the bounded queue was full.</summary>
    public long DroppedCount => Interlocked.Read(ref _dropped);

    /// <summary>Stops listening, then drains pending spans.</summary>
    public async ValueTask DisposeAsync()
    {
        _listener.Dispose();
        await _backbone.DisposeAsync().ConfigureAwait(false);
    }

    private void OnStopped(Activity activity)
    {
        if (activity.IdFormat != ActivityIdFormat.W3C || activity.TraceId == default)
            return;
        if (!_backbone.TryPublish(Render(activity)))
            Interlocked.Increment(ref _dropped);
    }

    private SpanDoc Render(Activity activity)
    {
        return new SpanDoc(
            activity.StartTimeUtc,
            Math.Round(activity.Duration.TotalMilliseconds, 3),
            activity.TraceId.ToHexString(),
            activity.SpanId.ToHexString(),
            activity.ParentSpanId == default ? null : activity.ParentSpanId.ToHexString(),
            SpanName(activity),
            activity.Kind switch
            {
                ActivityKind.Server => "server",
                ActivityKind.Client => "client",
                ActivityKind.Producer => "producer",
                ActivityKind.Consumer => "consumer",
                _ => "internal"
            },
            activity.Status == ActivityStatusCode.Error ? "error" : "ok",
            activity.Status == ActivityStatusCode.Error ? activity.StatusDescription : null,
            _options.ServiceName!,
            _options.ServiceVersion,
            HasTags(activity.TagObjects) ? activity.TagObjects : null,
            _options.IncludeBaggage && activity.Baggage.Any() ? activity.Baggage : null);
    }

    // "HTTP POST /api/x" beats the bare "Microsoft.AspNetCore.Hosting.HttpRequestIn"
    // operation name - graph consumers read name first. Kestrel leaves DisplayName
    // equal to OperationName, so tag-derived names take precedence for HTTP spans.
    private static string SpanName(Activity activity)
    {
        var method = (string?)null;
        var path = (string?)null;
        foreach (var tag in activity.TagObjects)
        {
            if (tag.Key is "http.request.method" or "http.method")
                method = tag.Value as string;
            else if (tag.Key is "url.path" or "http.target")
                path = tag.Value as string;
        }
        if (method is not null && path is not null)
            return string.Concat("HTTP ", method, " ", path);
        return string.IsNullOrEmpty(activity.DisplayName) ? activity.OperationName : activity.DisplayName;
    }

    // ICollection fast path avoids the enumerator allocation; the interface fallback
    // keeps Any() for sources that hand back a lazy sequence.
    private static bool HasTags(IEnumerable<KeyValuePair<string, object?>> tags)
        => tags is ICollection<KeyValuePair<string, object?>> collection
            ? collection.Count > 0
            : tags.Any();

    private async ValueTask WriteSpansAsync(ReadOnlyMemory<SpanDoc> batch, CancellationToken cancellationToken)
    {
        if (_docOutput is not null)
        {
            await _docOutput(batch, cancellationToken).ConfigureAwait(false);
            return;
        }
        var buffer = new ArrayBufferWriter<byte>(Math.Max(512, batch.Length * 320));
        for (var index = 0; index < batch.Length; index++)
        {
            WriteSpan(buffer, batch.Span[index]);
            buffer.GetSpan(1)[0] = (byte)'\n';
            buffer.Advance(1);
        }
        await _output!(buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
    }

    private static void WriteSpan(IBufferWriter<byte> buffer, in SpanDoc span)
    {
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteNumber("ts", span.StartedUtc.ToUnixTimeMilliseconds());
        writer.WriteNumber("durMs", span.DurationMilliseconds);
        writer.WriteString("tid", span.TraceId);
        writer.WriteString("sid", span.SpanId);
        WriteOpt(writer, "psid", span.ParentSpanId);
        writer.WriteString("name", span.Name);
        writer.WriteString("kind", span.Kind);
        writer.WriteString("status", span.Status);
        WriteOpt(writer, "err", span.Error);
        writer.WriteString("svc", span.ServiceName);
        WriteOpt(writer, "svc_ver", span.ServiceVersion);
        if (span.Tags is not null || span.Baggage is not null)
        {
            writer.WriteStartObject("tags");
            if (span.Tags is not null)
                foreach (var tag in span.Tags)
                    WriteTagValue(writer, tag.Key, tag.Value);
            if (span.Baggage is not null)
                foreach (var pair in span.Baggage)
                    WriteTagValue(writer, pair.Key, pair.Value);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        writer.Flush();
    }

    private static void WriteOpt(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is not null)
            writer.WriteString(name, value);
    }

    private static void WriteTagValue(Utf8JsonWriter writer, string key, object? value)
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
            case DateTimeOffset instant: writer.WriteString(key, instant); break;
            case DateTime instant: writer.WriteString(key, instant); break;
            default: writer.WriteString(key, value.ToString()); break;
        }
    }


    private sealed class Backbone(
        int capacity,
        int batchSize,
        Func<ReadOnlyMemory<SpanDoc>, CancellationToken, ValueTask> writer)
        : AsyncBackbone<SpanDoc>("pubsoft-spans", capacity, batchSize)
    {
        private readonly Func<ReadOnlyMemory<SpanDoc>, CancellationToken, ValueTask> _writer = writer;

        protected override ValueTask ProcessBatchAsync(ReadOnlyMemory<SpanDoc> batch, CancellationToken cancellationToken)
            => _writer(batch, cancellationToken);
    }
}
