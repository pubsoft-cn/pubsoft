using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PubSoft.Scheduling;


/// <summary>Type-keyed executor store.</summary>
public sealed class ExecutorRegistry : IExecutorRegistry
{
    private readonly ConcurrentDictionary<string, IJobExecutor> _executors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the registry from an executor set.</summary>
    public ExecutorRegistry(IEnumerable<IJobExecutor> executors)
    {
        foreach (var executor in executors)
            Register(executor);
    }

    /// <inheritdoc />
    public IJobExecutor? Resolve(string executorType)
        => _executors.TryGetValue(executorType, out var executor) ? executor : null;

    /// <inheritdoc />
    public void Register(IJobExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentException.ThrowIfNullOrWhiteSpace(executor.Type);
        _executors[executor.Type] = executor;
    }
}
