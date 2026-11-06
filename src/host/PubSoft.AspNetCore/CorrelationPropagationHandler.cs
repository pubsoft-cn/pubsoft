using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace PubSoft.AspNetCore;


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
