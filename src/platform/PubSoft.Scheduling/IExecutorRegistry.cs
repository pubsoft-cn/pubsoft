using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PubSoft.Scheduling;

public interface IExecutorRegistry
{
    IJobExecutor? Resolve(string executorType);
    void Register(IJobExecutor executor);
}
