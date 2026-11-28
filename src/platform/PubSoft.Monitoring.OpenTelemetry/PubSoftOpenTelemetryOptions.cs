namespace PubSoft.Monitoring.OpenTelemetry;

/// <summary>Options for the opinionated OpenTelemetry registration.</summary>
public sealed class PubSoftOpenTelemetryOptions
{
    /// <summary>Service name written to the OpenTelemetry resource. Required.</summary>
    public string? ServiceName { get; set; }

    /// <summary>Optional service version written to the OpenTelemetry resource.</summary>
    public string? ServiceVersion { get; set; }

    /// <summary>
    /// Absolute HTTP(S) OTLP endpoint. Null disables the exporter; setting it is the explicit
    /// export opt-in. Credentials, certificate policy, network egress, and collector availability
    /// remain host/operator responsibilities.
    /// </summary>
    public Uri? OtlpEndpoint { get; set; }

    /// <summary>Adds ASP.NET Core trace instrumentation. Default false.</summary>
    public bool IncludeAspNetCoreTraceInstrumentation { get; set; }

    /// <summary>Adds outbound HTTP client trace instrumentation. Default false.</summary>
    public bool IncludeHttpClientTraceInstrumentation { get; set; }

    /// <summary>Additional meter names to subscribe to beyond <see cref="MonitoringMetrics.MeterName"/>. Maximum 64 unique non-empty names.</summary>
    public List<string> AdditionalMeters { get; set; } = [];

    /// <summary>Additional activity source names to subscribe to. Maximum 64 unique non-empty names.</summary>
    public List<string> AdditionalActivitySources { get; set; } = [];

    /// <summary>Validates fail-closed before any provider is registered.</summary>
    internal bool IsValid(out string error)
    {
        if (string.IsNullOrWhiteSpace(ServiceName) || ServiceName.Length > 200 || HasControlCharacters(ServiceName))
        {
            error = "ServiceName is required, must be at most 200 characters, and must not contain control characters.";
            return false;
        }
        if (ServiceVersion is { Length: > 200 } || ServiceVersion is not null && HasControlCharacters(ServiceVersion))
        {
            error = "ServiceVersion must be at most 200 characters and must not contain control characters.";
            return false;
        }
        if (OtlpEndpoint is not null
            && (!OtlpEndpoint.IsAbsoluteUri || OtlpEndpoint.Scheme is not ("http" or "https")))
        {
            error = "OtlpEndpoint must be an absolute http or https URI.";
            return false;
        }
        if (!NamesValid(AdditionalMeters, nameof(AdditionalMeters), out error)
            || !NamesValid(AdditionalActivitySources, nameof(AdditionalActivitySources), out error))
            return false;
        error = string.Empty;
        return true;
    }

    private static bool NamesValid(List<string> names, string property, out string error)
    {
        if (names.Count > 64 || names.Any(string.IsNullOrWhiteSpace)
            || names.Any(HasControlCharacters)
            || new HashSet<string>(names, StringComparer.Ordinal).Count != names.Count)
        {
            error = $"{property} must contain at most 64 unique non-empty names without control characters.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool HasControlCharacters(string value)
        => value.Any(char.IsControl);
}
