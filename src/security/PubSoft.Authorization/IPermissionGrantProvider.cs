namespace PubSoft.Authorization;

public interface IPermissionGrantProvider
{
    Task<IReadOnlyList<PermissionGrant>> GetGrantsAsync(
        long subjectId,
        long? tenantId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PermissionGrant>> GetGrantsAsync(
        long subjectId,
        long? tenantId,
        string clientCode,
        CancellationToken cancellationToken = default)
        => string.IsNullOrEmpty(clientCode)
            ? GetGrantsAsync(subjectId, tenantId, cancellationToken)
            : Task.FromResult<IReadOnlyList<PermissionGrant>>(System.Array.Empty<PermissionGrant>());

}
