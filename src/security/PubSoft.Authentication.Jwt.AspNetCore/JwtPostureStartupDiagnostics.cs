using PubSoft.Monitoring;

namespace PubSoft.Authentication.Jwt.AspNetCore;

/// <summary>Reports JWT posture gaps the registration floor does not already reject.</summary>
public sealed class JwtPostureStartupDiagnostics : IStartupDiagnostics
{
    private readonly PubSoftJwtBearerOptions _options;

    /// <summary>Creates the provider over the configured options instance.</summary>
    public JwtPostureStartupDiagnostics(PubSoftJwtBearerOptions options) => _options = options;

    /// <inheritdoc />
    public IEnumerable<StartupDiagnosticsEntry> Collect()
    {
        if (string.IsNullOrWhiteSpace(_options.Audience))
            yield return new StartupDiagnosticsEntry("jwt-audience",
                StartupDiagnosticsStatus.Warning,
                "JWT audience is empty - tokens without an aud claim may be accepted; configure the intended audience");
    }
}
