using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace PubSoft.AspNetCore;

/// <summary>
/// Cross-service correlation — accepts W3C <c>traceparent</c> or a custom
/// correlation header; assigns <see cref="HttpContext.TraceIdentifier"/>,
/// echoes the id on the response, and exposes it for outbound propagation.
/// <para>
/// Design: upstream (edge gateway/reverse proxy) generates a trace id when the
/// client sends none; this middleware adopts it so every hop logs under the same
/// key. Outbound HTTP calls can re-emit <see cref="CorrelationIdMiddleware.CurrentId(HttpContext)"/>
/// as <c>traceparent</c>/<c>X-Correlation-Id</c> to keep the chain intact.
/// </para>
/// </summary>
public sealed class CorrelationIdOptions
{
    /// <summary>Response header carrying the resolved id. Default <c>X-Correlation-Id</c>.</summary>
    public string ResponseHeaderName { get; set; } = "X-Correlation-Id";

    /// <summary>Additional accepted inbound header names (checked after traceparent).</summary>
    public string[] InboundHeaderNames { get; set; } = ["X-Correlation-Id", "X-Request-Id"];

    /// <summary>Echo the id on the response header. Default true.</summary>
    public bool EchoResponseHeader { get; set; } = true;

    /// <summary>Enabled switch — default true.</summary>
    public bool Enabled { get; set; } = true;
}

public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly CorrelationIdOptions _options;

    public CorrelationIdMiddleware(RequestDelegate next, CorrelationIdOptions? options = null)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _options = options ?? new CorrelationIdOptions();
    }

    /// <summary>Resolved correlation id for the current request — use this when
    /// propagating to outbound calls (same id, whichever inbound form arrived).</summary>
    public static string CurrentId(HttpContext context) => context.TraceIdentifier;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled)
        {
            await _next(context);
            return;
        }

        var id = ResolveInbound(context) ?? context.TraceIdentifier;
        context.TraceIdentifier = id;
        // ambient: (traceId, 本服务spanId) — 出栈 DelegatingHandler 读此续链
        CorrelationAmbient.Current = (id, CorrelationAmbient.NewSpanId());

        if (_options.EchoResponseHeader)
        {
            context.Response.OnStarting(static state =>
            {
                var (ctx, header) = ((HttpContext, string))state;
                ctx.Response.Headers.TryAdd(header, new StringValues(ctx.TraceIdentifier));
                return Task.CompletedTask;
            }, (context, _options.ResponseHeaderName));
        }

        await _next(context);
    }

    /// <summary>Inbound resolution order: valid <c>traceparent</c> (take trace-id
    /// segment) → each configured header in order → null (caller keeps default).</summary>
    private string? ResolveInbound(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("traceparent", out var tp)
            && tp.Count > 0 && TryParseTraceId(tp[0], out var traceId))
            return traceId;

        foreach (var name in _options.InboundHeaderNames)
        {
            if (context.Request.Headers.TryGetValue(name, out var v)
                && v.Count > 0 && !string.IsNullOrWhiteSpace(v[0]))
                return v[0]!.Trim();
        }
        return null;
    }

    /// <summary>W3C <c>traceparent</c>: <c>version-traceId-spanId-flags</c>,
    /// takes the 32-hex trace-id segment when the shape is valid.</summary>
    private static bool TryParseTraceId(string? value, out string traceId)
    {
        traceId = "";
        if (string.IsNullOrEmpty(value) || value.Length < 35)
            return false;
        // 00-<32hex>-<16hex>-<2hex> — segment 1 must be 32 lowercase/uppercase hex
        var seg = value.AsSpan(3, 32);
        foreach (var c in seg)
            if (!Uri.IsHexDigit(c)) return false;
        traceId = seg.ToString().ToLowerInvariant();
        return true;
    }
}

public static class CorrelationIdMiddlewareExtensions
{
    /// <summary>Register correlation-id adoption + echo — place before any
    /// middleware/logging that should share the id.</summary>
    public static IApplicationBuilder UsePubSoftCorrelationId(
        this IApplicationBuilder app, CorrelationIdOptions? options = null)
        => app.UseMiddleware<CorrelationIdMiddleware>(options);
}

/// <summary>Ambient correlation state — AsyncLocal flows to outbound async calls
/// without IHttpContextAccessor registration. Middleware sets; handler reads.</summary>
public static class CorrelationAmbient
{
    private static readonly System.Threading.AsyncLocal<(string TraceId, string SpanId)?> _current = new();

    /// <summary>Current (traceId, spanId) pair — null outside a request scope.</summary>
    public static (string TraceId, string SpanId)? Current
    {
        get => _current.Value;
        internal set => _current.Value = value;
    }

    /// <summary>Shared g53 snowflake generator — worker id derived from machine name hash,
    /// collision-free across processes on one host.</summary>
    private static readonly PubSoft.Core.SnowflakeIdGenerator _gen = new(
        (Math.Abs(Environment.MachineName.GetHashCode()) % 1024));

    /// <summary>New span id — g53 snowflake → 16-hex (W3C spanId slot).</summary>
    public static string NewSpanId() => _gen.NextId().ToString("x16");
}

/// <summary>Outbound propagation handler — stamps the ambient trace onto outbound
/// requests so the next hop sees the same trace-id (parent = this service's span).
/// <para>Usage: <c>services.AddHttpClient("x").AddHttpMessageHandler&lt;CorrelationPropagationHandler&gt;()</c></para></summary>
public sealed class CorrelationPropagationHandler : DelegatingHandler
{
    /// <summary>Also emit the resolved id under this header (default <c>X-Correlation-Id</c>);
    /// set null to emit traceparent only.</summary>
    public string? CorrelationHeaderName { get; set; } = "X-Correlation-Id";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (CorrelationAmbient.Current is { } cur)
        {
            // 下游视本服务 spanId 为父 — DAG 连续边
            request.Headers.TryAddWithoutValidation("traceparent", $"00-{cur.TraceId}-{cur.SpanId}-01");
            if (CorrelationHeaderName is { } h)
                request.Headers.TryAddWithoutValidation(h, cur.TraceId);
        }
        return base.SendAsync(request, cancellationToken);
    }
}
