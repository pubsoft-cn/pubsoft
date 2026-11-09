using PubSoft.Monitoring;
using PubSoft.Scheduling;

namespace PubSoft.Monitoring.Scheduling;


/// <summary>Condition that fires a scheduled-job alert rule.</summary>
public enum AlertConditionType
{
    /// <summary>Job health flipped to degraded.</summary>
    StateDegraded,
    /// <summary>Consecutive failures crossed the rule threshold.</summary>
    StateConfirmed,
    /// <summary>Job recovered after a degraded or confirmed state.</summary>
    StateRecovered,
    /// <summary>Run exceeded the rule duration bound.</summary>
    SlowResponse,
    /// <summary>First observed failure for the job.</summary>
    FirstFailure,
    /// <summary>Success after at least one failure.</summary>
    Recovered,
    /// <summary>Every failed run fires the rule.</summary>
    EveryFailure
}
