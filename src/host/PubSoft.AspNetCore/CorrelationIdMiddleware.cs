using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace PubSoft.AspNetCore;


/// <summary>Propagates a correlation id through the request via <see cref="HttpContext.TraceIdentifier"/>.</summary>
public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly CorrelationIdOptions _options;

    /// <summary>Creates the middleware.</summary>
    public CorrelationIdMiddleware(RequestDelegate next, CorrelationIdOptions? options = null)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _options = options ?? new CorrelationIdOptions();
    }

    /// <summary>Resolved correlation id for the current request -- use this when
    /// propagating to outbound calls (same id, whichever inbound form arrived).</summary>
    public static string CurrentId(HttpContext context) => context.TraceIdentifier;

    /// <summary>Sets the trace identifier then forwards.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var (id, inboundSpanId) = ResolveInbound(context);
        id ??= context.TraceIdentifier;
        context.TraceIdentifier = id;
        // ambient fallback triple: trace id, this request's span id (the real W3C span when
        // an Activity exists, a fresh id otherwise), and the inbound caller's span id so the
        // parent chain stays intact for non-Activity hops.
        var activity = System.Diagnostics.Activity.Current;
        var spanId = activity is { IdFormat: System.Diagnostics.ActivityIdFormat.W3C }
            ? activity.SpanId.ToHexString()
            : CorrelationAmbient.NewSpanId();
        CorrelationAmbient.Current = (id, spanId, inboundSpanId);

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

    /// <summary>Inbound resolution order: valid <c>traceparent</c> (trace-id + parent
    /// span-id segments) -> each configured header in order -> null (caller keeps default).</summary>
    private (string? Id, string? ParentSpanId) ResolveInbound(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("traceparent", out var tp)
            && tp.Count > 0 && TryParseTraceParent(tp[0], out var traceId, out var spanId))
            return (traceId, spanId);

        foreach (var name in _options.InboundHeaderNames)
        {
            if (context.Request.Headers.TryGetValue(name, out var v)
                && v.Count > 0 && !string.IsNullOrWhiteSpace(v[0]))
                return (v[0]!.Trim(), null);
        }
        return (null, null);
    }

    /// <summary>W3C <c>traceparent</c>: <c>version-traceId-spanId-flags</c> -- extracts the
    /// 32-hex trace id and the 16-hex caller span id (the parent's span, needed so downstream
    /// spans can point their psid at a real edge).</summary>
    private static bool TryParseTraceParent(string? value, out string traceId, out string? parentSpanId)
    {
        traceId = "";
        parentSpanId = null;
        if (string.IsNullOrEmpty(value) || value.Length < 55)
            return false;
        // 00-<32hex>-<16hex>-<2hex>
        var traceSeg = value.AsSpan(3, 32);
        var spanSeg = value.AsSpan(36, 16);
        var needsLower = false;
        foreach (var c in traceSeg)
        {
            if (!Uri.IsHexDigit(c)) return false;
            needsLower |= c is >= 'A' and <= 'F';
        }
        foreach (var c in spanSeg)
        {
            if (!Uri.IsHexDigit(c)) return false;
            needsLower |= c is >= 'A' and <= 'F';
        }
        // Lowercase only when needed - inbound traceparents are lowercase in practice.
        traceId = needsLower ? traceSeg.ToString().ToLowerInvariant() : traceSeg.ToString();
        parentSpanId = needsLower ? spanSeg.ToString().ToLowerInvariant() : spanSeg.ToString();
        return true;
    }
}
