using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace PubSoft.Scheduling;


public interface IResultProcessorRegistry
{
    IResultProcessor? Resolve(string name);
    void Register(IResultProcessor processor);
}
