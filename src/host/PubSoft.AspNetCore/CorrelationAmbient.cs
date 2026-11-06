using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace PubSoft.AspNetCore;


/// <summary>Ambient correlation state -- AsyncLocal flows to outbound async calls
/// without IHttpContextAccessor registration. Middleware sets; handler reads.</summary>
public static class CorrelationAmbient
{
    private static readonly System.Threading.AsyncLocal<(string TraceId, string SpanId)?> _current = new();

    /// <summary>Shared g53 snowflake generator -- worker id derived from machine name hash,
    /// collision-free across processes on one host.</summary>
    private static readonly PubSoft.Core.SnowflakeIdGenerator _gen = new(
        Math.Abs(StringComparer.Ordinal.GetHashCode(Environment.MachineName)) % (PubSoft.Core.SnowflakeIdGenerator.MaxWorkerId + 1));

    /// <summary>Current (traceId, spanId) pair -- null outside a request scope.</summary>
    public static (string TraceId, string SpanId)? Current
    {
        get => _current.Value;
        internal set => _current.Value = value;
    }

    /// <summary>New span id -- g53 snowflake -> 16-hex (W3C spanId slot).</summary>
    public static string NewSpanId() => _gen.NextId().ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
}
