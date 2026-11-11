using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PubSoft.Monitoring;


/// <summary>
/// Aggregates every registered <see cref="IStartupDiagnostics"/> into a startup
/// snapshot. Emit via <see cref="ToJson"/> -- a machine-readable posture report the
/// host can log once at boot or expose on an ops endpoint.
/// </summary>
public sealed class StartupDiagnosticsCollector
{
    private readonly IEnumerable<IStartupDiagnostics> _providers;

    /// <summary>Creates the collector.</summary>
    public StartupDiagnosticsCollector(IEnumerable<IStartupDiagnostics> providers)
        => _providers = providers ?? throw new System.ArgumentNullException(nameof(providers));

    /// <summary>Returns every provider's entries.</summary>
    public IReadOnlyList<StartupDiagnosticsEntry> Collect()
    {
        var entries = new List<StartupDiagnosticsEntry>();
        foreach (var provider in _providers)
            entries.AddRange(provider.Collect());
        return entries;
    }

    /// <summary>JSON report: <c>{"overall":"ok|warning|error","entries":[...]}</c>.
    /// Serialized via System.Text.Json so provider details with control characters,
    /// quotes or unicode stay parseable.</summary>
    public string ToJson()
    {
        var entries = Collect();
        var overall = entries.Any(e => e.Status == StartupDiagnosticsStatus.Error) ? "error"
            : entries.Any(e => e.Status == StartupDiagnosticsStatus.Warning) ? "warning" : "ok";
        // Utf8JsonWriter: correct escaping, zero reflection - AOT-safe.
        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("overall", overall);
            writer.WriteStartArray("entries");
            foreach (var e in entries)
            {
                writer.WriteStartObject();
                writer.WriteString("area", e.Area);
                writer.WriteString("status", e.Status switch
                {
                    StartupDiagnosticsStatus.Warning => "warning",
                    StartupDiagnosticsStatus.Error => "error",
                    _ => "ok"
                });
                writer.WriteString("detail", e.Detail);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
