using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Auditing;
using PubSoft.Domain;

namespace PubSoft.EntityFrameworkCore.Auditing;

public sealed class AuditLogRecord : IMultiTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long? TenantId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public string? HttpMethod { get; set; }
    public string? RequestPath { get; set; }
    public string? ClientIp { get; set; }
    public string? UserAgent { get; set; }
    public string? SubjectId { get; set; }
    public string? SubjectName { get; set; }
    public bool? IsAdministrator { get; set; }
    public bool Success { get; set; }
    public int StatusCode { get; set; }
    public string? FailureCode { get; set; }
    public long DurationMilliseconds { get; set; }
    public string? Category { get; set; }
    public string? Action { get; set; }
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string? TargetName { get; set; }
    public string? Detail { get; set; }
    public ICollection<AuditEntityChangeRecord> EntityChanges { get; } = new List<AuditEntityChangeRecord>();
}

public sealed class AuditEntityChangeRecord : IMultiTenant
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AuditLogId { get; set; }
    public long? TenantId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public AuditChangeKind Kind { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public string PropertiesJson { get; set; } = "[]";
    public AuditLogRecord AuditLog { get; set; } = null!;
}

public static class AuditPersistenceModelBuilderExtensions
{
    private static readonly ValueConverter<DateTimeOffset, long> UtcTicksConverter = new(
        instant => instant.UtcTicks,
        ticks => new DateTimeOffset(ticks, TimeSpan.Zero));

    public static ModelBuilder ConfigurePubSoftAuditPersistence(this ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<AuditLogRecord>(ConfigureAuditLog);
        modelBuilder.Entity<AuditEntityChangeRecord>(ConfigureEntityChange);
        return modelBuilder;
    }

    private static void ConfigureAuditLog(EntityTypeBuilder<AuditLogRecord> builder)
    {
        builder.ToTable("PubSoftAuditLogs");
        builder.HasKey(record => record.Id);
        builder.HasIndex(record => new { record.TenantId, record.StartedAt });
        builder.HasIndex(record => new { record.TenantId, record.Category, record.Action });
        builder.HasIndex(record => new { record.TenantId, record.SubjectId, record.StartedAt });
        builder.Property(record => record.StartedAt).HasConversion(UtcTicksConverter);
        builder.Property(record => record.HttpMethod).HasMaxLength(16);
        builder.Property(record => record.RequestPath).HasMaxLength(2_048);
        builder.Property(record => record.ClientIp).HasMaxLength(128);
        builder.Property(record => record.UserAgent).HasMaxLength(2_048);
        builder.Property(record => record.SubjectId).HasMaxLength(256);
        builder.Property(record => record.SubjectName).HasMaxLength(512);
        builder.Property(record => record.FailureCode).HasMaxLength(128);
        builder.Property(record => record.Category).HasMaxLength(128);
        builder.Property(record => record.Action).HasMaxLength(128);
        builder.Property(record => record.TargetType).HasMaxLength(256);
        builder.Property(record => record.TargetId).HasMaxLength(256);
        builder.Property(record => record.TargetName).HasMaxLength(512);
        builder.Property(record => record.Detail).HasMaxLength(16_384);
        builder.HasMany(record => record.EntityChanges)
            .WithOne(change => change.AuditLog)
            .HasForeignKey(change => change.AuditLogId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureEntityChange(EntityTypeBuilder<AuditEntityChangeRecord> builder)
    {
        builder.ToTable("PubSoftAuditEntityChanges");
        builder.HasKey(change => change.Id);
        builder.HasIndex(change => new { change.TenantId, change.AuditLogId });
        builder.Property(change => change.ChangedAt).HasConversion(UtcTicksConverter);
        builder.Property(change => change.EntityType).HasMaxLength(256).IsRequired();
        builder.Property(change => change.EntityId).HasMaxLength(512).IsRequired();
        builder.Property(change => change.PropertiesJson).IsRequired();
    }
}

public sealed class EfCoreAuditSnapshotSink<TDbContext>(TDbContext dbContext) : IAuditSnapshotSink
    where TDbContext : DbContext
{
    public async ValueTask WriteAsync(AuditRequestSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var record = new AuditLogRecord
        {
            StartedAt = snapshot.Request.StartedAt,
            HttpMethod = snapshot.Request.HttpMethod,
            RequestPath = snapshot.Request.RequestPath,
            ClientIp = snapshot.Request.ClientIp,
            UserAgent = snapshot.Request.UserAgent,
            SubjectId = snapshot.Request.Actor?.SubjectId,
            SubjectName = snapshot.Request.Actor?.DisplayName,
            IsAdministrator = snapshot.Request.Actor?.IsAdministrator,
            Success = snapshot.Success,
            StatusCode = snapshot.StatusCode,
            FailureCode = snapshot.FailureCode,
            DurationMilliseconds = Math.Max(0, (long)snapshot.Duration.TotalMilliseconds),
            Category = snapshot.Business.Category,
            Action = snapshot.Business.Action,
            TargetType = snapshot.Business.TargetType,
            TargetId = snapshot.Business.TargetId,
            TargetName = snapshot.Business.TargetName,
            Detail = snapshot.Business.Detail
        };

        foreach (var change in snapshot.Changes)
        {
            record.EntityChanges.Add(new AuditEntityChangeRecord
            {
                EntityType = change.EntityType,
                EntityId = change.EntityId,
                Kind = change.Kind,
                ChangedAt = change.ChangedAt,
                PropertiesJson = JsonSerializer.Serialize(change.Properties)
            });
        }

        dbContext.Set<AuditLogRecord>().Add(record);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

public static class AuditPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPubSoftEfCoreAuditPersistence<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IAuditSnapshotSink, EfCoreAuditSnapshotSink<TDbContext>>();
        services.AddScoped<IAuditLogWriter, AuditLogWriter>();
        return services;
    }
}