using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Auditing;
using PubSoft.Domain;

namespace PubSoft.EntityFrameworkCore.Auditing;


/// <summary>EF audit sink writing request snapshots plus entity changes with Snowflake ids.</summary>
public sealed class EfCoreAuditSnapshotSink<TDbContext>(TDbContext dbContext, PubSoft.Core.IIdGenerator idGenerator) : IAuditSnapshotSink
    where TDbContext : DbContext
{
    /// <inheritdoc />
    public async ValueTask WriteAsync(AuditRequestSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var record = new AuditLogRecord
        {
            Id = idGenerator.NextId(),
            StartedAt = snapshot.Request.StartedAt,
            HttpMethod = Truncate(snapshot.Request.HttpMethod, 16),
            RequestPath = Truncate(snapshot.Request.RequestPath, 2_048),
            ClientIp = Truncate(snapshot.Request.ClientIp, 128),
            UserAgent = Truncate(snapshot.Request.UserAgent, 2_048),
            SubjectId = Truncate(snapshot.Request.Actor?.SubjectId, 256),
            SubjectName = Truncate(snapshot.Request.Actor?.DisplayName, 512),
            IsAdministrator = snapshot.Request.Actor?.IsAdministrator,
            Success = snapshot.Success,
            StatusCode = snapshot.StatusCode,
            FailureCode = Truncate(snapshot.FailureCode, 128),
            DurationMilliseconds = Math.Max(0, (long)snapshot.Duration.TotalMilliseconds),
            Category = Truncate(snapshot.Business.Category, 128),
            Action = Truncate(snapshot.Business.Action, 128),
            TargetType = Truncate(snapshot.Business.TargetType, 256),
            TargetId = Truncate(snapshot.Business.TargetId, 256),
            TargetName = Truncate(snapshot.Business.TargetName, 512),
            Detail = Truncate(snapshot.Business.Detail, 16_384)
        };

        foreach (var change in snapshot.Changes)
        {
            record.EntityChanges.Add(new AuditEntityChangeRecord
            {
                Id = idGenerator.NextId(),
                EntityType = Truncate(change.EntityType, 256)!,
                EntityId = Truncate(change.EntityId, 512)!,
                Kind = change.Kind,
                ChangedAt = change.ChangedAt,
                PropertiesJson = JsonSerializer.Serialize(change.Properties)
            });
        }

        dbContext.Set<AuditLogRecord>().Add(record);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Truncates overlong fields to the column cap -- better a truncated trail than dropping the whole audit record.</summary>
    private static string? Truncate(string? value, int maxLength)
        => value is null || value.Length <= maxLength ? value : value[..maxLength];
}
