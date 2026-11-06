using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PubSoft.Scheduling;


/// <summary>Name-keyed result-processor store.</summary>
public sealed class ResultProcessorRegistry : IResultProcessorRegistry
{
    private readonly ConcurrentDictionary<string, IResultProcessor> _processors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the registry from a processor set.</summary>
    public ResultProcessorRegistry(IEnumerable<IResultProcessor> processors)
    {
        foreach (var processor in processors)
            Register(processor);
    }

    /// <inheritdoc />
    public IResultProcessor? Resolve(string name)
        => _processors.TryGetValue(name, out var processor) ? processor : null;

    /// <inheritdoc />
    public void Register(IResultProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentException.ThrowIfNullOrWhiteSpace(processor.Name);
        _processors[processor.Name] = processor;
    }
}
