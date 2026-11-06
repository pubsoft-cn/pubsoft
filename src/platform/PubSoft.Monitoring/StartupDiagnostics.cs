using System.Text;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PubSoft.Monitoring;

public enum StartupDiagnosticsStatus { Ok, Warning, Error }

/// <summary>One posture fact a package wants surfaced at startup — name, verdict, and detail.</summary>
public readonly record struct StartupDiagnosticsEntry(
    string Area, StartupDiagnosticsStatus Status, string Detail);

/// <summary>
/// Contributes deployment-posture entries to the startup report — register once per
/// package: weak keys, insecure opt-ins actually enabled, missing audience config,
/// process-local nonce store on a multi-replica host, etc.
/// </summary>
public interface IStartupDiagnostics
{
    IEnumerable<StartupDiagnosticsEntry> Collect();
}

/// <summary>
/// Aggregates every registered <see cref="IStartupDiagnostics"/> into a startup
/// snapshot. Emit via <see cref="ToJson"/> — a machine-readable posture report the
/// host can log once at boot or expose on an ops endpoint.
/// </summary>
public sealed class StartupDiagnosticsCollector
{
    private readonly IEnumerable<IStartupDiagnostics> _providers;

    public StartupDiagnosticsCollector(IEnumerable<IStartupDiagnostics> providers)
        => _providers = providers ?? throw new System.ArgumentNullException(nameof(providers));

    public IReadOnlyList<StartupDiagnosticsEntry> Collect()
    {
        var entries = new List<StartupDiagnosticsEntry>();
        foreach (var provider in _providers)
            entries.AddRange(provider.Collect());
        return entries;
    }

    /// <summary>JSON report: <c>{"overall":"ok|warning|error","entries":[...]}</c>.</summary>
    public string ToJson()
    {
        var entries = Collect();
        var overall = entries.Any(e => e.Status == StartupDiagnosticsStatus.Error) ? "error"
            : entries.Any(e => e.Status == StartupDiagnosticsStatus.Warning) ? "warning" : "ok";
        var sb = new StringBuilder(128 + entries.Count * 80);
        sb.Append("{\"overall\":\"").Append(overall).Append("\",\"entries\":[");
        for (var i = 0; i < entries.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var e = entries[i];
            sb.Append("{\"area\":\"").Append(Esc(e.Area))
              .Append("\",\"status\":\"").Append(e.Status.ToString().ToLowerInvariant()) // pubsoft:exempt PUBSOFT027 "enum name render on non-hot path (doc-gen/startup/log line)"
              .Append("\",\"detail\":\"").Append(Esc(e.Detail)).Append("\"}");
        }
        return sb.Append(']').Append('}').ToString();
    }

    private static string Esc(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n"); // pubsoft:exempt PUBSOFT022 "char overload cannot do 1-to-N replacement"
}

public static class StartupDiagnosticsServiceCollectionExtensions
{
    /// <summary>Registers the <see cref="StartupDiagnosticsCollector"/> and one provider instance.</summary>
    public static IServiceCollection AddPubSoftStartupDiagnostics<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
            this IServiceCollection services)
        where T : class, IStartupDiagnostics
    {
        services.TryAddSingleton<StartupDiagnosticsCollector>();
        services.AddSingleton<IStartupDiagnostics, T>();
        return services;
    }
}
