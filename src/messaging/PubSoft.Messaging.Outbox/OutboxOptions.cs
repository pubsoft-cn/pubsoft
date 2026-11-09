using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;

namespace PubSoft.Messaging.Outbox;


/// <summary>Outbox polling, retry, and lease tuning.</summary>
public sealed class OutboxOptions
{
    /// <summary>Messages fetched per poll.</summary>
    public int BatchSize { get; set; } = 50;
    /// <summary>Delivery attempts before dead-lettering.</summary>
    public int MaxAttempts { get; set; } = 5;
    /// <summary>Payload size bound; larger events fail at publish.</summary>
    public int MaximumPayloadCharacters { get; set; } = 1_048_576;
    /// <summary>Processing lease length; expired leases are reclaimable.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(1);
    /// <summary>Delay between polling passes.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>First retry delay; subsequent attempts back off exponentially.</summary>
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Retry delay ceiling.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromHours(1);

    internal OutboxOptions CloneAndValidate()
    {
        if (BatchSize is < 1 or > 1_000)
            throw new ArgumentOutOfRangeException(nameof(BatchSize));
        if (MaxAttempts is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(MaxAttempts));
        if (MaximumPayloadCharacters is < 1 or > 16_777_216)
            throw new ArgumentOutOfRangeException(nameof(MaximumPayloadCharacters));
        if (LeaseDuration <= TimeSpan.Zero || LeaseDuration > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(LeaseDuration));
        if (PollInterval <= TimeSpan.Zero || PollInterval > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(PollInterval));
        if (BaseRetryDelay <= TimeSpan.Zero || MaxRetryDelay < BaseRetryDelay || MaxRetryDelay > TimeSpan.FromDays(7))
            throw new ArgumentOutOfRangeException(nameof(BaseRetryDelay));

        return new OutboxOptions
        {
            BatchSize = BatchSize,
            MaxAttempts = MaxAttempts,
            MaximumPayloadCharacters = MaximumPayloadCharacters,
            LeaseDuration = LeaseDuration,
            PollInterval = PollInterval,
            BaseRetryDelay = BaseRetryDelay,
            MaxRetryDelay = MaxRetryDelay
        };
    }
}
