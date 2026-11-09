using PubSoft.Monitoring;
using PubSoft.Scheduling;

namespace PubSoft.Monitoring.Scheduling;


/// <summary>Evaluation input for one job run: current result plus the state transition under review.</summary>
public sealed class AlertContext
{
    /// <summary>The job that ran.</summary>
    public required ScheduledJob Job { get; init; }
    /// <summary>Result of the current run.</summary>
    public required JobResult Current { get; init; }
    /// <summary>Derived health before this run.</summary>
    public JobHealthState FromState { get; init; }
    /// <summary>Derived health after this run.</summary>
    public JobHealthState ToState { get; init; }
    /// <summary>Probe outcome attached to the run.</summary>
    public ProbeHealth ProbeHealth { get; init; }
    /// <summary>Whether the job probe endpoint was unreachable.</summary>
    public bool WasProbeUnreachable { get; init; }
}
