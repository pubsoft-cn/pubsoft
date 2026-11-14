namespace PubSoft.Events;

/// <summary>Marker for a domain event.</summary>
public interface IDomainEvent
{
    /// <summary>When the event occurred.</summary>
    DateTimeOffset OccurredOn { get; }
}
