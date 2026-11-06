using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace PubSoft.AspNetCore;


public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly CorrelationIdOptions _options;

    public CorrelationIdMiddleware(RequestDelegate next, CorrelationIdOptions? options = null)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _options = options ?? new CorrelationIdOptions();
    }

    /// <summary>Resolved correlation id for the current request -- use this when
    /// propagating to outbound calls (same id, whichever inbound form arrived).</summary>
    public static string CurrentId(HttpContext context) => context.TraceIdentifier;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var id = ResolveInbound(context) ?? context.TraceIdentifier;
        context.TraceIdentifier = id;
        // ambient: (traceId, 本服务spanId) - 出栈 DelegatingHandler 读此续链
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

        await _next(context).ConfigureAwait(false);
    }

    /// <summary>Inbound resolution order: valid <c>traceparent</c> (take trace-id
    /// segment) -> each configured header in order -> null (caller keeps default).</summary>
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
        // 00-<32hex>-<16hex>-<2hex> - segment 1 must be 32 lowercase/uppercase hex
        var seg = value.AsSpan(3, 32);
        foreach (var c in seg)
            if (!Uri.IsHexDigit(c)) return false;
        traceId = seg.ToString().ToLowerInvariant();
        return true;
    }
}
