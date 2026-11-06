using PubSoft.Monitoring;
using PubSoft.Scheduling;

namespace PubSoft.Monitoring.Scheduling;


public sealed class AlertProcessorOptions
{
    public int FailureThreshold { get; set; } = 3;

    internal bool IsValid() => FailureThreshold > 0;
}
