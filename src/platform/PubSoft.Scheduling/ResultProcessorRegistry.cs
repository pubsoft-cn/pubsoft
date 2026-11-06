using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PubSoft.Scheduling;


public sealed class ResultProcessorRegistry : IResultProcessorRegistry
{
    private readonly ConcurrentDictionary<string, IResultProcessor> _processors = new(StringComparer.OrdinalIgnoreCase);

    public ResultProcessorRegistry(IEnumerable<IResultProcessor> processors)
    {
        foreach (var processor in processors)
            Register(processor);
    }

    public IResultProcessor? Resolve(string name)
        => _processors.TryGetValue(name, out var processor) ? processor : null;

    public void Register(IResultProcessor processor)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentException.ThrowIfNullOrWhiteSpace(processor.Name);
        _processors[processor.Name] = processor;
    }
}
