namespace PubSoft.Authorization;

/// <summary>Resolves a subject's permission grants (implemented by the host).</summary>
public interface IPermissionGrantProvider
{
    /// <summary>Returns the subject's grants.</summary>
    Task<IReadOnlyList<PermissionGrant>> GetGrantsAsync(
    /// <summary>Returns the subject's grants, tenant-scoped.</summary>
        long subjectId,
        long? tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the subject's grants for a client code.</summary>
    Task<IReadOnlyList<PermissionGrant>> GetGrantsAsync(
    /// <summary>Returns the subject's grants for a client code.</summary>
        long subjectId,
        long? tenantId,
        string clientCode,
        CancellationToken cancellationToken = default)
        => string.IsNullOrEmpty(clientCode)
            ? GetGrantsAsync(subjectId, tenantId, cancellationToken)
            : Task.FromResult<IReadOnlyList<PermissionGrant>>(System.Array.Empty<PermissionGrant>());

}
