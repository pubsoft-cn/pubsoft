using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Monitoring;

namespace PubSoft.AspNetCore;

/// <summary>Health-endpoint mapping extensions.</summary>
public static class HealthEndpointRouteBuilderExtensions
{
    /// <summary>Default health endpoint path.</summary>
    public const string DefaultPath = "/health";

    /// <summary>Maps an opt-in aggregate health endpoint with no per-check details.</summary>
    /// <remarks>Apply authorization and rate-limiting conventions to the returned endpoint builder as required by the host.</remarks>
    public static IEndpointConventionBuilder MapPubSoftHealthEndpoint(
        this IEndpointRouteBuilder endpoints,
        string pattern = DefaultPath)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        RequestDelegate handler = async context =>
        {
            var aggregator = context.RequestServices.GetRequiredService<HealthCheckAggregator>();
            var status = await aggregator.GetOverallStatusAsync(context.RequestAborted).ConfigureAwait(false);
            context.Response.StatusCode = status == AppHealthStatus.Unhealthy
                ? StatusCodes.Status503ServiceUnavailable
                : StatusCodes.Status200OK;
            await context.Response.WriteAsJsonAsync(
                new { status = status.ToString() }, // pubsoft:exempt PUBSOFT027 "enum name render on non-hot path (doc-gen/startup/log line)"
                context.RequestAborted).ConfigureAwait(false);
        };

        return endpoints.MapGet(pattern, handler);
    }
}