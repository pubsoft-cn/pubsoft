using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PubSoft.Scheduling;


public sealed class ExecutorRegistry : IExecutorRegistry
{
    private readonly ConcurrentDictionary<string, IJobExecutor> _executors = new(StringComparer.OrdinalIgnoreCase);

    public ExecutorRegistry(IEnumerable<IJobExecutor> executors)
    {
        foreach (var executor in executors)
            Register(executor);
    }

    public IJobExecutor? Resolve(string executorType)
        => _executors.TryGetValue(executorType, out var executor) ? executor : null;

    public void Register(IJobExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentException.ThrowIfNullOrWhiteSpace(executor.Type);
        _executors[executor.Type] = executor;
    }
}
