namespace PubSoft.Monitoring;

/// <summary>Posture verdict for one diagnostics entry.</summary>
public enum StartupDiagnosticsStatus
{
    /// <summary>Healthy posture.</summary>
    Ok,
    /// <summary>Warning posture.</summary>
    Warning,
    /// <summary>Failing posture.</summary>
    Error
}
