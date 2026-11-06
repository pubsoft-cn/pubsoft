using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using PubSoft.Auditing;

namespace PubSoft.EntityFrameworkCore;

public sealed class AuditChangeCaptureHook : ISaveChangesHook
{
    private readonly IAuditChangeCollector _collector;
    private readonly AuditChangeCollectorOptions _options;
    private readonly TimeProvider _timeProvider;

    public AuditChangeCaptureHook(
        IAuditChangeCollector collector,
        AuditChangeCollectorOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _options = (options ?? new AuditChangeCollectorOptions()).CloneAndValidate();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public void BeforeSaveChanges(DbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var changes = new List<AuditEntityChange>();

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Detached or EntityState.Unchanged)
                continue;

            var entityType = entry.Metadata.ClrType.Name;
            if (_options.IgnoredEntityTypes.Contains(entityType))
                continue;

            var kind = entry.State switch
            {
                EntityState.Added => AuditChangeKind.Created,
                EntityState.Modified => AuditChangeKind.Updated,
                EntityState.Deleted => AuditChangeKind.Deleted,
                _ => (AuditChangeKind?)null
            };
            if (kind is null)
                continue;

            var properties = CaptureProperties(entry, kind.Value);
            changes.Add(new AuditEntityChange(
                entityType,
                GetEntityId(entry),
                kind.Value,
                _timeProvider.GetUtcNow(),
                Array.AsReadOnly(properties.ToArray())));
        }

        _collector.AddRange(changes);
    }

    private IReadOnlyList<AuditPropertyChange> CaptureProperties(EntityEntry entry, AuditChangeKind kind)
    {
        var properties = new List<AuditPropertyChange>();
        foreach (var property in entry.Properties)
        {
            if (IsAuditMetadata(property.Metadata.Name))
                continue;

            var changed = kind switch
            {
                AuditChangeKind.Created => true,
                AuditChangeKind.Updated => property.IsModified,
                AuditChangeKind.Deleted => true,
                _ => false
            };
            if (!changed)
                continue;

            if (!_options.CaptureValues)
            {
                properties.Add(new AuditPropertyChange(property.Metadata.Name, null, null));
            }
            else if (_options.SensitiveProperties.Contains(property.Metadata.Name)
                || property.Metadata.PropertyInfo?.GetCustomAttributes(typeof(PubSoft.Auditing.AuditSensitiveAttribute), inherit: true).Any() == true)
            {
                var redacted = AuditValueSerializer.Serialize("[REDACTED]", _options.MaxStringValueLength);
                properties.Add(new AuditPropertyChange(property.Metadata.Name,
                    kind is AuditChangeKind.Updated or AuditChangeKind.Deleted ? redacted : null,
                    kind is AuditChangeKind.Created or AuditChangeKind.Updated ? redacted : null));
            }
            else
            {
                properties.Add(new AuditPropertyChange(
                    property.Metadata.Name,
                    kind is AuditChangeKind.Updated or AuditChangeKind.Deleted
                        ? AuditValueSerializer.Serialize(property.OriginalValue, _options.MaxStringValueLength)
                        : null,
                    kind is AuditChangeKind.Created or AuditChangeKind.Updated
                        ? AuditValueSerializer.Serialize(property.CurrentValue, _options.MaxStringValueLength)
                        : null));
            }

            if (properties.Count >= _options.MaxChangedPropertiesPerEntity)
                break;
        }

        return properties;
    }

    private static string GetEntityId(EntityEntry entry)
    {
        var key = entry.Metadata.FindPrimaryKey();
        if (key is null)
            return string.Empty;

        return string.Join(",", key.Properties.Select(property =>
            entry.OriginalValues[property] ?? entry.CurrentValues[property] ?? ""));
    }

    private static bool IsAuditMetadata(string propertyName)
        => propertyName is "CreatorId" or "CreationTime" or "LastModifierId"
            or "LastModificationTime" or "IsDeleted" or "DeleterId" or "DeletionTime"
            or "TenantId" or "Id";
}