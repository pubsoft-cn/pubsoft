using System.Diagnostics;

namespace PubSoft.Tracing;

/// <summary>Options for <see cref="PubSoftSpanExporter"/>.</summary>
public sealed class PubSoftTracingOptions
{
    /// <summary>Service identity written as <c>svc</c> on every span and used in the
    /// <c>spans-{service}-*.jsonl</c> file name. Required.</summary>
    public string? ServiceName { get; set; }

    /// <summary>Optional version written as <c>svc_ver</c>.</summary>
    public string? ServiceVersion { get; set; }

    /// <summary>Directory for <c>spans-{service}-yyyy-MM-dd.jsonl</c> output (UTC-day rollover).
    /// Exactly one of <see cref="Directory"/>, <see cref="Output"/> or <see cref="DocOutput"/>
    /// is required.</summary>
    public string? Directory { get; set; }

    /// <summary>Custom batch output (jsonl bytes). Alternative to <see cref="Directory"/> for
    /// sinks that take the default shape as-is (fan-out, remote shipping).</summary>
    public Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? Output { get; set; }

    /// <summary>Structured batch output - receives rendered <see cref="SpanDoc"/>s before
    /// serialization, for sinks that emit their own document shape (e.g. an indexer-specific
    /// contract) without a byte-level parse-and-rewrite round trip.</summary>
    public Func<ReadOnlyMemory<SpanDoc>, CancellationToken, ValueTask>? DocOutput { get; set; }

    /// <summary>Bounded in-flight span queue; spans are dropped (and counted) past this point.
    /// Default 10_000.</summary>
    public int Capacity { get; set; } = 10_000;

    /// <summary>Flush batch size. Default 64.</summary>
    public int BatchSize { get; set; } = 64;

    /// <summary>Activity sources to listen to; default listens to all. Narrow this to keep
    /// noisy third-party sources out of the span file.</summary>
    public Func<ActivitySource, bool>? ShouldListenTo { get; set; }

    /// <summary>Sampling decision handed to the listener; default records everything
    /// (<see cref="ActivitySamplingResult.AllDataAndRecorded"/>) so dev-box span files are
    /// complete. Return <see cref="ActivitySamplingResult.PropagationData"/> or
    /// <see cref="ActivitySamplingResult.None"/> to thin volume.</summary>
    public ActivitySamplingResult Sampling { get; set; } = ActivitySamplingResult.AllDataAndRecorded;

    /// <summary>Emit <c>activity.Baggage</c> entries into the span <c>tags</c> object.
    /// Default false - baggage can carry caller-controlled PII.</summary>
    public bool IncludeBaggage { get; set; }

    /// <summary>Validates the option set; the exporter calls this before listening.</summary>
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ServiceName);
        if (ServiceName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("ServiceName must be file-name safe.", nameof(ServiceName));
        var outputs = (Directory is null ? 0 : 1) + (Output is null ? 0 : 1) + (DocOutput is null ? 0 : 1);
        if (outputs != 1)
            throw new ArgumentException("Exactly one of Directory, Output or DocOutput is required.", nameof(Output));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Capacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(BatchSize);
    }
}
