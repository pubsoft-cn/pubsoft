using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Monitoring;

namespace PubSoft.AspNetCore;

/// <summary>Endpoint mapping for the startup-diagnostics report.</summary>
public static class StartupDiagnosticsRouteBuilderExtensions
{
    /// <summary>Default diagnostics endpoint path.</summary>
    public const string DefaultPath = "/startup-diagnostics";

    /// <summary>
    /// Maps an opt-in endpoint returning the collected posture entries as JSON
    /// (<c>[{"area":"...","status":"Warning","detail":"..."}]</c>).
    /// Apply authorization/rate-limiting conventions to the returned builder as required by the host.
    /// </summary>
    public static IEndpointConventionBuilder MapPubSoftStartupDiagnostics(
        this IEndpointRouteBuilder endpoints,
        string pattern = DefaultPath)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapGet(pattern, (StartupDiagnosticsCollector collector) =>
            Results.Json(collector.Collect().Select(e => new
            {
                area = e.Area,
                status = e.Status switch
                {
                    PubSoft.Monitoring.StartupDiagnosticsStatus.Warning => "Warning",
                    PubSoft.Monitoring.StartupDiagnosticsStatus.Error => "Error",
                    _ => "Ok"
                },
                detail = e.Detail
            }), new JsonSerializerOptions { WriteIndented = false }));
    }
}
