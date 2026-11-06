namespace PubSoft.Monitoring;

/// <summary>Configuration for the HTTP probe health check.</summary>
public sealed class MonitoringOptions
{
    public const string HttpClientName = "PubSoft.Monitoring.Probe";

    /// <summary>Absolute HTTP(S) endpoints checked sequentially. Empty means no external dependency is configured.</summary>
    public List<string> ProbeTargets { get; set; } = [];

    /// <summary>Minimum fraction of successful targets required for a healthy result.</summary>
    public double MinProbeSuccessRatio { get; set; } = 0.5;

    /// <summary>Maximum duration of one target request.</summary>
    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(3);

    internal bool IsValid(out string error)
    {
        if (ProbeTargets is null)
        {
            error = "ProbeTargets cannot be null.";
            return false;
        }

        if (double.IsNaN(MinProbeSuccessRatio) || MinProbeSuccessRatio is < 0 or > 1)
        {
            error = "MinProbeSuccessRatio must be between 0 and 1.";
            return false;
        }

        if (ProbeTimeout <= TimeSpan.Zero)
        {
            error = "ProbeTimeout must be greater than zero.";
            return false;
        }

        foreach (var target in ProbeTargets)
        {
            if (!Uri.TryCreate(target, UriKind.Absolute, out var uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttp, System.StringComparison.Ordinal) && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, System.StringComparison.Ordinal)
                || !string.IsNullOrEmpty(uri.UserInfo))
            {
                error = "Each probe target must be an absolute HTTP(S) URL without embedded credentials.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }
}