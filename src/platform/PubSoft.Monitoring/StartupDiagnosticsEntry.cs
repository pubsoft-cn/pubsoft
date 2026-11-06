using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PubSoft.Monitoring;


/// <summary>One posture fact a package wants surfaced at startup — name, verdict, and detail.</summary>
public readonly record struct StartupDiagnosticsEntry(
    string Area, StartupDiagnosticsStatus Status, string Detail);
