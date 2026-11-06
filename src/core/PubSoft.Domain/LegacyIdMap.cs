namespace PubSoft.Domain;

/// <summary>
/// Migration shim mapping a legacy Guid id to its replacement Snowflake id, scoped
/// by entity type. Used while migrating rows (and external references that already
/// hold the old id) from Guid keys to long keys. Retire the table once the
/// migration completes.
/// </summary>
public sealed class LegacyIdMap : Entity<long>, IMultiTenant
{
    /// <summary>Parameterless constructor for ORM materialization.</summary>
    /// <summary>
    /// Creates a mapping row; <paramref name="entityType"/> must be non-empty and
    /// trimmed, ids must be positive, and <paramref name="legacyId"/> must not be empty.
    /// </summary>
    public LegacyIdMap(long id, string entityType, Guid legacyId, long newId, long? tenantId = null)
    {
        if (id <= 0 || newId <= 0)
            throw new ArgumentOutOfRangeException(nameof(id), "Ids must be positive Snowflake ids.");
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        if (!string.Equals(entityType, entityType.Trim(), System.StringComparison.Ordinal))
            throw new ArgumentException("Entity type names must have no surrounding whitespace.", nameof(entityType));
        if (legacyId == Guid.Empty)
            throw new ArgumentException("Legacy id must not be empty.", nameof(legacyId));
        if (tenantId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(tenantId));
        Id = id;
        EntityType = entityType;
        LegacyId = legacyId;
        NewId = newId;
        TenantId = tenantId;
    }

    private LegacyIdMap()
    {
    }


    /// <summary>Owning tenant; null for host-level rows.</summary>
    public long? TenantId { get; set; }
    /// <summary>The entity type this mapping applies to (e.g. "user", "order").</summary>
    public string EntityType { get; private set; } = string.Empty;
    /// <summary>The retired Guid id.</summary>
    public Guid LegacyId { get; private set; }
    /// <summary>The replacement Snowflake id.</summary>
    public long NewId { get; private set; }
}
