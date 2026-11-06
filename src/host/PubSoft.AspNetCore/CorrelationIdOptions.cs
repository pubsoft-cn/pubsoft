using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace PubSoft.AspNetCore;

/// <summary>
/// Cross-service correlation -- accepts W3C <c>traceparent</c> or a custom
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

    /// <summary>Enabled switch -- default true.</summary>
    public bool Enabled { get; set; } = true;
}
