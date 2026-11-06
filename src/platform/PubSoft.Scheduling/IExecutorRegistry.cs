using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PubSoft.Scheduling;

/// <summary>Resolves executors by type name.</summary>
public interface IExecutorRegistry
{
    /// <summary>Finds an executor by type.</summary>
    IJobExecutor? Resolve(string executorType);
    /// <summary>Adds an executor.</summary>
    void Register(IJobExecutor executor);
}
