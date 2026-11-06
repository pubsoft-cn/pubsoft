using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PubSoft.Monitoring;


/// <summary>
/// Contributes deployment-posture entries to the startup report -- register once per
/// package: weak keys, insecure opt-ins actually enabled, missing audience config,
/// process-local nonce store on a multi-replica host, etc.
/// </summary>
public interface IStartupDiagnostics
{
    /// <summary>Returns this package's posture entries.</summary>
    IEnumerable<StartupDiagnosticsEntry> Collect();
}
