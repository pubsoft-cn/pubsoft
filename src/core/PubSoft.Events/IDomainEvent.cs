using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PubSoft.Events;

/// <summary>Marker for a domain event.</summary>
public interface IDomainEvent
{
    /// <summary>When the event occurred.</summary>
    DateTimeOffset OccurredOn { get; }
}
