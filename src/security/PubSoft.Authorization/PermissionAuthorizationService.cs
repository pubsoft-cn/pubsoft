namespace PubSoft.Authorization;


/// <summary>Evaluates whether a subject holds required permissions via grants.</summary>
public sealed class PermissionAuthorizationService
{
    private readonly PermissionDefinitionRegistry _definitions;
    private readonly IPermissionGrantProvider _grants;

    /// <summary>Creates the service over the definition registry and grant provider.</summary>
    public PermissionAuthorizationService(
        PermissionDefinitionRegistry definitions,
        IPermissionGrantProvider grants)
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _grants = grants ?? throw new ArgumentNullException(nameof(grants));
    }

    /// <summary>Whether the subject holds the required flag bits on a module.</summary>
    public async Task<bool> IsGrantedAsync(
        long subjectId,
        long? tenantId,
        string moduleKey,
        PermissionFlags requiredFlags,
        CancellationToken cancellationToken = default)
        => await IsGrantedAsync(subjectId, tenantId, string.Empty, moduleKey, requiredFlags, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Whether the subject holds the required flag bits on a module.</summary>
    public async Task<bool> IsGrantedAsync(
        long subjectId,
        long? tenantId,
        string clientCode,
        string moduleKey,
        PermissionFlags requiredFlags,
        CancellationToken cancellationToken = default)
    {
        if (subjectId <= 0 || tenantId is <= 0)
            return false;
        ValidateClientCode(clientCode, nameof(clientCode));
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
        if (requiredFlags == PermissionFlags.None || (requiredFlags & ~PermissionFlags.All) != 0)
            return false;

        var moduleDefinitions = _definitions.GetByModule(moduleKey, clientCode);
        if (moduleDefinitions.Count == 0)
            return false;

        var definedFlags = moduleDefinitions.Aggregate(
            PermissionFlags.None,
            (current, definition) => current | definition.Flags);
        if ((definedFlags & requiredFlags) != requiredFlags)
            return false;

        var subjectGrants = await GetModuleGrantsAsync(subjectId, tenantId, clientCode, moduleKey, cancellationToken)
            .ConfigureAwait(false);
        var grantedFlags = subjectGrants
            .Aggregate(PermissionFlags.None, (current, grant) => current | grant.Flags);

        return (grantedFlags & requiredFlags) == requiredFlags;
    }

    /// <summary>Whether the subject holds a named permission.</summary>
    public async Task<bool> IsCustomPermissionGrantedAsync(
        long subjectId,
        long? tenantId,
        string moduleKey,
        string permissionName,
        CancellationToken cancellationToken = default)
        => await IsCustomPermissionGrantedAsync(
            subjectId, tenantId, string.Empty, moduleKey, permissionName, cancellationToken).ConfigureAwait(false);

    /// <summary>Whether the subject holds a named permission.</summary>
    public async Task<bool> IsCustomPermissionGrantedAsync(
        long subjectId,
        long? tenantId,
        string clientCode,
        string moduleKey,
        string permissionName,
        CancellationToken cancellationToken = default)
    {
        if (subjectId <= 0 || tenantId is <= 0)
            return false;
        ValidateClientCode(clientCode, nameof(clientCode));
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(permissionName);
        if (!string.Equals(permissionName, permissionName.Trim(), System.StringComparison.Ordinal))
            throw new ArgumentException("Custom permission names must have no surrounding whitespace.", nameof(permissionName));

        var moduleDefinitions = _definitions.GetByModule(moduleKey, clientCode);
        if (!moduleDefinitions.Any(definition => (definition.Flags & PermissionFlags.Custom) != 0))
            return false;

        var subjectGrants = await GetModuleGrantsAsync(subjectId, tenantId, clientCode, moduleKey, cancellationToken)
            .ConfigureAwait(false);
        return subjectGrants.Any(grant => grant.CustomPermissions.Contains(permissionName, StringComparer.Ordinal));
    }

    /// <summary>Whether the subject holds an extension flag bit.</summary>
    public async Task<bool> IsExtensionPermissionGrantedAsync(
        long subjectId,
        long? tenantId,
        string moduleKey,
        uint requiredExtensionFlags,
        CancellationToken cancellationToken = default)
        => await IsExtensionPermissionGrantedAsync(
            subjectId, tenantId, string.Empty, moduleKey, requiredExtensionFlags, cancellationToken).ConfigureAwait(false);

    /// <summary>Whether the subject holds an extension flag bit.</summary>
    public async Task<bool> IsExtensionPermissionGrantedAsync(
        long subjectId,
        long? tenantId,
        string clientCode,
        string moduleKey,
        uint requiredExtensionFlags,
        CancellationToken cancellationToken = default)
    {
        if (subjectId <= 0 || tenantId is <= 0)
            return false;
        ValidateClientCode(clientCode, nameof(clientCode));
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
        if (requiredExtensionFlags == 0)
            return false;

        var moduleDefinitions = _definitions.GetByModule(moduleKey, clientCode);
        var definedExtensionFlags = moduleDefinitions
            .Where(definition => definition.IsExtPermission)
            .Aggregate(0u, (current, definition) => current | definition.ExtFlag);
        if ((definedExtensionFlags & requiredExtensionFlags) != requiredExtensionFlags)
            return false;

        var subjectGrants = await GetModuleGrantsAsync(subjectId, tenantId, clientCode, moduleKey, cancellationToken)
            .ConfigureAwait(false);
        var grantedExtensionFlags = subjectGrants
            .Aggregate(0u, (current, grant) => current | grant.ExtensionFlags);
        return (grantedExtensionFlags & requiredExtensionFlags) == requiredExtensionFlags;
    }

    private async Task<IReadOnlyList<PermissionGrant>> GetModuleGrantsAsync(
        long subjectId,
        long? tenantId,
        string clientCode,
        string moduleKey,
        CancellationToken cancellationToken)
    {
        var subjectGrants = await _grants.GetGrantsAsync(subjectId, tenantId, clientCode, cancellationToken)
            .ConfigureAwait(false);
        return subjectGrants?
            .Where(grant => grant is not null && grant.TenantId == tenantId
                && string.Equals(grant.ClientCode, clientCode, StringComparison.Ordinal)
                && string.Equals(grant.ModuleKey, moduleKey, StringComparison.Ordinal))
            .ToArray() ?? [];
    }

    private static void ValidateClientCode(string clientCode, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(clientCode, parameterName);
        if (clientCode.Length > 64 || !string.Equals(clientCode, clientCode.Trim(), System.StringComparison.Ordinal))
            throw new ArgumentException("Client codes must be trimmed and at most 64 characters.", parameterName);
    }
}
