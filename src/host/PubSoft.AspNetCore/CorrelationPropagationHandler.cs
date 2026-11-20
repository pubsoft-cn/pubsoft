using System.Diagnostics;

namespace PubSoft.AspNetCore;


/// <summary>Outbound propagation handler -- stamps the trace context onto outbound
/// requests so the next hop sees the same trace-id, parented at a real recorded span.
/// <para>
/// The canonical source is <see cref="Activity.Current"/>: inside a request it is the
/// ASP.NET <c>HttpRequestIn</c> span derived from the inbound traceparent, so the
/// downstream's psid points at a span this service actually emitted. The ambient
/// <see cref="CorrelationAmbient"/> triple is the fallback for flows with no Activity.
/// </para>
/// <para>Usage: <c>services.AddHttpClient("x").AddHttpMessageHandler&lt;CorrelationPropagationHandler&gt;()</c></para></summary>
public sealed class CorrelationPropagationHandler : DelegatingHandler
{
    /// <summary>Also emit the resolved id under this header (default <c>X-Correlation-Id</c>);
    /// set null to emit traceparent only.</summary>
    public string? CorrelationHeaderName { get; set; } = "X-Correlation-Id";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var activity = Activity.Current;
        if (activity is { IdFormat: ActivityIdFormat.W3C })
        {
            Stamp(request, activity.TraceId.ToHexString(), activity.SpanId.ToHexString(),
                activity.ActivityTraceFlags.HasFlag(ActivityTraceFlags.Recorded));
        }
        else if (CorrelationAmbient.Current is { } cur)
        {
            // No Activity - the ambient span id is the best parent the chain can offer.
            Stamp(request, cur.TraceId, cur.SpanId, sampled: true);
        }
        return base.SendAsync(request, cancellationToken);
    }

    private void Stamp(HttpRequestMessage request, string traceId, string spanId, bool sampled)
    {
        request.Headers.TryAddWithoutValidation("traceparent", $"00-{traceId}-{spanId}-{(sampled ? "01" : "00")}");
        if (CorrelationHeaderName is { } header)
            request.Headers.TryAddWithoutValidation(header, traceId);
    }
}
