namespace PubSoft.Monitoring.Scheduling;


/// <summary>Tuning for the alert-rule processor.</summary>
public sealed class AlertProcessorOptions
{
    /// <summary>Consecutive failures before the confirmed state fires.</summary>
    public int FailureThreshold { get; set; } = 3;

    internal bool IsValid() => FailureThreshold > 0;
}
