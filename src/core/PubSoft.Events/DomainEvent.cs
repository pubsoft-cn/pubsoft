using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PubSoft.Events;


/// <summary>Base class for domain events with an occurrence timestamp.</summary>
[PubSoft.Core.BoundedCost("domain entity - timestamps assigned by EF interceptors/domain services, not injected TimeProvider")]
public abstract class DomainEvent : IDomainEvent
{
    /// <summary>When the event occurred (UTC).</summary>
    public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;
}
