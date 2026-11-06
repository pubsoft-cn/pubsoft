using PubSoft.Monitoring;
using PubSoft.Scheduling;

namespace PubSoft.Monitoring.Scheduling;


public sealed class AlertContext
{
    public required ScheduledJob Job { get; init; }
    public required JobResult Current { get; init; }
    public JobHealthState FromState { get; init; }
    public JobHealthState ToState { get; init; }
    public ProbeHealth ProbeHealth { get; init; }
    public bool WasProbeUnreachable { get; init; }
}
