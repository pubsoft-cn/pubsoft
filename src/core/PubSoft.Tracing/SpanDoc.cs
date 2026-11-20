namespace PubSoft.Tracing;

/// <summary>One stopped W3C activity as a structured span document - the unit handed to
/// <see cref="PubSoftTracingOptions.DocOutput"/> before serialization. <c>TraceId</c> +
/// <c>SpanId</c> uniquely identify the span; <c>ParentSpanId</c> links it into the trace tree.</summary>
/// <param name="StartedUtc">Activity start instant.</param>
/// <param name="DurationMilliseconds">Measured duration, rounded to 3 decimals.</param>
/// <param name="TraceId">W3C trace id (32-hex).</param>
/// <param name="SpanId">W3C span id (16-hex).</param>
/// <param name="ParentSpanId">Caller span id; null on roots.</param>
/// <param name="Name">Display name (HTTP route spans become <c>HTTP {method} {path}</c>).</param>
/// <param name="Kind">Activity kind: <c>server</c>/<c>client</c>/<c>producer</c>/<c>consumer</c>/<c>internal</c>.</param>
/// <param name="Status"><c>ok</c> or <c>error</c>.</param>
/// <param name="Error">Status description when <see cref="Status"/> is <c>error</c>.</param>
/// <param name="ServiceName">Emitting service (<c>svc</c>).</param>
/// <param name="ServiceVersion">Optional emitting version (<c>svc_ver</c>).</param>
/// <param name="Tags">Activity tag objects when present.</param>
/// <param name="Baggage">Activity baggage when <c>IncludeBaggage</c> opted in.</param>
public readonly record struct SpanDoc(
    DateTimeOffset StartedUtc,
    double DurationMilliseconds,
    string TraceId,
    string SpanId,
    string? ParentSpanId,
    string Name,
    string Kind,
    string Status,
    string? Error,
    string ServiceName,
    string? ServiceVersion,
    IEnumerable<KeyValuePair<string, object?>>? Tags,
    IEnumerable<KeyValuePair<string, string?>>? Baggage);
