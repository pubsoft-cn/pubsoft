using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PubSoft.Events;


/// <summary>Options for the event dispatch channel.</summary>
public sealed class DomainEventOptions
{
    /// <summary>Bounded publish-channel capacity (1-100000); producers wait when full.</summary>
    public int Capacity { get; set; } = 1_000;

    internal int ValidateAndGetCapacity()
    {
        if (Capacity is < 1 or > 100_000)
            throw new ArgumentOutOfRangeException(nameof(Capacity), "Event capacity must be between 1 and 100000.");
        return Capacity;
    }
}
