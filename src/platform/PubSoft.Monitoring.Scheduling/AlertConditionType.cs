using PubSoft.Monitoring;
using PubSoft.Scheduling;

namespace PubSoft.Monitoring.Scheduling;


public enum AlertConditionType
{
    StateDegraded,
    StateConfirmed,
    StateRecovered,
    SlowResponse,
    FirstFailure,
    Recovered,
    EveryFailure
}
