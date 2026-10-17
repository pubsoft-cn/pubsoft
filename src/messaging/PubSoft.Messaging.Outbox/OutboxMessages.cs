using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;

namespace PubSoft.Messaging.Outbox;

public enum OutboxMessageStatus
{
    Pending,
    Processing,
    Delivered,
    RetryScheduled,
    DeadLetter
}

public sealed class OutboxMessage : IMultiTenant
{
    private OutboxMessage() { }

    public Guid Id { get; private set; }
    public long? TenantId { get; set; }
    public string EventType { get; private set; } = string.Empty;
    public string PayloadJson { get; private set; } = string.Empty;
    public string? TargetModule { get; private set; }
    public string? AggregateKey { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public OutboxMessageStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public Guid? LeaseToken { get; private set; }
    public DateTimeOffset? LeaseExpiresAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public string? LastErrorCode { get; private set; }

    internal OutboxMessage(
        Guid id,
        string eventType,
        string payloadJson,
        string? targetModule,
        string? aggregateKey,
        DateTimeOffset occurredAt,
        int maxAttempts)
    {
        // 列上限对齐 OnModelCreating——超长原样入库会整批 SaveChanges 失败，宁可截断留痕
        Id = id;
        EventType = Truncate(eventType, 512)!;
        PayloadJson = payloadJson;
        TargetModule = Truncate(targetModule, 128);
        AggregateKey = Truncate(aggregateKey, 256);
        OccurredAt = occurredAt;
        Status = OutboxMessageStatus.Pending;
        MaxAttempts = maxAttempts;
    }

    private static string? Truncate(string? value, int maxLength)
        => value is null || value.Length <= maxLength ? value : value[..maxLength];

    internal void AcquireLease(Guid leaseToken, DateTimeOffset now, TimeSpan leaseDuration)
    {
        Status = OutboxMessageStatus.Processing;
        AttemptCount++;
        LeaseToken = leaseToken;
        LeaseExpiresAt = now + leaseDuration;
        NextAttemptAt = null;
    }

    internal void MarkDeadLetter(string errorCode)
    {
        Status = OutboxMessageStatus.DeadLetter;
        LeaseToken = null;
        LeaseExpiresAt = null;
        NextAttemptAt = null;
        LastErrorCode = errorCode;
    }
}

public sealed class InboxReceipt : IMultiTenant
{
    private InboxReceipt() { }

    public long? TenantId { get; set; }
    public string TenantKey { get; private set; } = string.Empty;
    public string ConsumerName { get; private set; } = string.Empty;
    public Guid MessageId { get; private set; }
    public DateTimeOffset ProcessedAt { get; private set; }

    internal InboxReceipt(long? tenantId, string consumerName, Guid messageId, DateTimeOffset processedAt)
    {
        TenantId = tenantId;
        TenantKey = tenantId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "host";
        ConsumerName = consumerName;
        MessageId = messageId;
        ProcessedAt = processedAt;
    }
}

public interface IOutboxPublisher
{
    Task<Guid> PublishAsync<TEvent>(
        TEvent @event,
        string? targetModule = null,
        string? aggregateKey = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull;
}

public interface IInboxMessageProcessor
{
    Task<bool> ProcessOnceAsync(
        string consumerName,
        Guid messageId,
        Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken = default);
}

public static class OutboxModelBuilderExtensions
{
    private static readonly ValueConverter<DateTimeOffset, long> UtcTicksConverter = new(
        instant => instant.UtcTicks,
        ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

    public static ModelBuilder ConfigurePubSoftOutbox(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.Entity<OutboxMessage>(ConfigureOutboxMessage);
        modelBuilder.Entity<InboxReceipt>(ConfigureInboxReceipt);
        return modelBuilder;
    }

    private static void ConfigureOutboxMessage(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("PubSoftOutboxMessages");
        builder.HasKey(message => message.Id);
        builder.HasIndex(message => new { message.TenantId, message.Status, message.OccurredAt });
        builder.HasIndex(message => new { message.TenantId, message.AggregateKey, message.OccurredAt });
        builder.Property(message => message.OccurredAt).HasConversion(UtcTicksConverter);
        builder.Property(message => message.NextAttemptAt).HasConversion(UtcTicksConverter);
        builder.Property(message => message.LeaseExpiresAt).HasConversion(UtcTicksConverter);
        builder.Property(message => message.DeliveredAt).HasConversion(UtcTicksConverter);
        builder.Property(message => message.EventType).HasMaxLength(512).IsRequired();
        builder.Property(message => message.PayloadJson).IsRequired();
        builder.Property(message => message.TargetModule).HasMaxLength(128);
        builder.Property(message => message.AggregateKey).HasMaxLength(256);
        builder.Property(message => message.LastErrorCode).HasMaxLength(128);
    }

    private static void ConfigureInboxReceipt(EntityTypeBuilder<InboxReceipt> builder)
    {
        builder.ToTable("PubSoftInboxReceipts");
        builder.HasKey(receipt => new { receipt.TenantKey, receipt.ConsumerName, receipt.MessageId });
        builder.Property(receipt => receipt.TenantKey).HasMaxLength(32).IsRequired();
        builder.Property(receipt => receipt.ConsumerName).HasMaxLength(128).IsRequired();
        builder.Property(receipt => receipt.ProcessedAt).HasConversion(UtcTicksConverter);
    }
}

public sealed class OutboxOptions
{
    public int BatchSize { get; set; } = 50;
    public int MaxAttempts { get; set; } = 5;
    public int MaximumPayloadCharacters { get; set; } = 1_048_576;
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(1);
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(30);
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

public sealed class EfCoreOutboxPublisher<TDbContext> : IOutboxPublisher
    where TDbContext : PubSoftDbContext
{
    private readonly TDbContext _dbContext;
    private readonly OutboxOptions _options;
    private readonly TimeProvider _timeProvider;

    public EfCoreOutboxPublisher(TDbContext dbContext, OutboxOptions? options = null, TimeProvider? timeProvider = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _options = (options ?? new OutboxOptions()).CloneAndValidate();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<Guid> PublishAsync<TEvent>(
        TEvent @event,
        string? targetModule = null,
        string? aggregateKey = null,
        CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(@event);
        cancellationToken.ThrowIfCancellationRequested();

        var eventType = @event.GetType();
        var payload = System.Text.Json.JsonSerializer.Serialize(@event, eventType);
        if (payload.Length > _options.MaximumPayloadCharacters)
            throw new ArgumentOutOfRangeException(nameof(@event), "The serialized event payload exceeds the configured limit.");

        var message = new OutboxMessage(
            Guid.NewGuid(),
            eventType.FullName ?? eventType.Name,
            payload,
            targetModule,
            aggregateKey,
            _timeProvider.GetUtcNow(),
            _options.MaxAttempts);
        _dbContext.Set<OutboxMessage>().Add(message);
        return Task.FromResult(message.Id);
    }
}

public static class OutboxServiceCollectionExtensions
{
    public static IServiceCollection AddPubSoftOutbox<TDbContext>(
        this IServiceCollection services,
        Action<OutboxOptions>? configure = null)
        where TDbContext : PubSoftDbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new OutboxOptions();
        configure?.Invoke(options);
        var validatedOptions = options.CloneAndValidate();
        services.TryAddSingleton(validatedOptions);
        services.TryAddScoped<IOutboxPublisher, EfCoreOutboxPublisher<TDbContext>>();
        services.TryAddScoped<IOutboxStore, EfCoreOutboxStore<TDbContext>>();
        services.TryAddScoped<IInboxMessageProcessor, EfCoreInboxMessageProcessor<TDbContext>>();
        services.TryAddScoped<OutboxProcessor>();
        return services;
    }

    public static IServiceCollection AddPubSoftOutboxWorker(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<Microsoft.Extensions.Hosting.IHostedService, OutboxBackgroundService>());
        return services;
    }
}