using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PubSoft.Monitoring;


/// <summary>One posture fact a package wants surfaced at startup -- name, verdict, and detail.</summary>
/// <param name="Area">Reporting area.</param>
/// <param name="Status">Posture verdict.</param>
/// <param name="Detail">Human-readable detail.</param>
public readonly record struct StartupDiagnosticsEntry(
    string Area, StartupDiagnosticsStatus Status, string Detail);
