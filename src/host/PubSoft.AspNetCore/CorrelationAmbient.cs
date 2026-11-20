namespace PubSoft.AspNetCore;


/// <summary>Ambient correlation state -- AsyncLocal flows to outbound async calls
/// without IHttpContextAccessor registration. Middleware sets; handler reads.
/// <para>
/// <c>Activity.Current</c> is the canonical trace context inside requests (ASP.NET
/// derives it from the inbound traceparent, so its span id is a real recorded span).
/// The ambient pair is the fallback for flows with no Activity - background work
/// that still wants the chain intact.
/// </para></summary>
public static class CorrelationAmbient
{
    private static readonly System.Threading.AsyncLocal<(string TraceId, string SpanId, string? ParentSpanId)?> _current = new();

    /// <summary>Shared g53 snowflake generator -- worker id mixes machine name hash and
    /// process id so same-host processes do not collide. Unsigned arithmetic: avoids
    /// Math.Abs(int.MinValue) overflow and keeps the result in range. Cross-host
    /// collision is possible but harmless (span ids are per-trace random).</summary>
    private static readonly PubSoft.Core.SnowflakeIdGenerator _gen = new(unchecked(
        (long)((uint)(StringComparer.Ordinal.GetHashCode(Environment.MachineName) * 397 + Environment.ProcessId)
            % ((ulong)PubSoft.Core.SnowflakeIdGenerator.MaxWorkerId + 1))));

    /// <summary>Current (traceId, spanId, parentSpanId) triple -- null outside a request scope.
    /// <c>ParentSpanId</c> is the inbound caller's span id; <c>SpanId</c> is what outbound
    /// hops should see as their parent.</summary>
    public static (string TraceId, string SpanId, string? ParentSpanId)? Current
    {
        get => _current.Value;
        internal set => _current.Value = value;
    }

    /// <summary>New span id -- g53 snowflake -> 16-hex (W3C spanId slot).</summary>
    public static string NewSpanId() => _gen.NextId().ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
}
