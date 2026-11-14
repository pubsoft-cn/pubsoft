namespace PubSoft.Messaging.Outbox;

/// <summary>Outbox delivery state.</summary>
public enum OutboxMessageStatus
{
    /// <summary>Published, awaiting dispatch.</summary>
    Pending,
    /// <summary>Lease acquired; a worker holds the message.</summary>
    Processing,
    /// <summary>Dispatched successfully.</summary>
    Delivered,
    /// <summary>Failed attempt; waiting for the next retry time.</summary>
    RetryScheduled,
    /// <summary>Exhausted max attempts; requires manual intervention.</summary>
    DeadLetter
}
