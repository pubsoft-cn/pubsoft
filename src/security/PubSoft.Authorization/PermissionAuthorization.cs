namespace PubSoft.Authorization;

public sealed class PermissionGrant
{
    public PermissionGrant(
        long? tenantId,
        string moduleKey,
        PermissionFlags flags,
        uint extensionFlags = 0,
        IEnumerable<string>? customPermissions = null,
        string clientCode = "")
    {
        if (tenantId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(tenantId), "Tenant ID must be positive when specified.");
        ArgumentException.ThrowIfNullOrWhiteSpace(moduleKey);
        if (moduleKey != moduleKey.Trim())
            throw new ArgumentException("Permission module keys must have no surrounding whitespace.", nameof(moduleKey));
        ValidateClientCode(clientCode, nameof(clientCode));
        if ((flags & ~PermissionFlags.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(flags), "Permission grants may contain only defined flag bits.");
        var customPermissionSnapshot = (customPermissions ?? []).ToArray();
        if (customPermissionSnapshot.Any(permission => string.IsNullOrWhiteSpace(permission)
            || permission != permission.Trim()))
            throw new ArgumentException("Custom permission names must be non-empty and have no surrounding whitespace.", nameof(customPermissions));
        if (flags == PermissionFlags.None && extensionFlags == 0 && customPermissionSnapshot.Length == 0)
            throw new ArgumentException("A permission grant must contain at least one permission.");

        TenantId = tenantId;
        ClientCode = clientCode;
        ModuleKey = moduleKey;
        Flags = flags;
        ExtensionFlags = extensionFlags;
        CustomPermissions = Array.AsReadOnly(customPermissionSnapshot);
    }

    public long? TenantId { get; }
    public string ClientCode { get; }
    public string ModuleKey { get; }
    public PermissionFlags Flags { get; }
    public uint ExtensionFlags { get; }
    public IReadOnlyList<string> CustomPermissions { get; }

    private static void ValidateClientCode(string clientCode, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(clientCode, parameterName);
        if (clientCode.Length > 64 || clientCode != clientCode.Trim())
            throw new ArgumentException("Client codes must be trimmed and at most 64 characters.", parameterName);
    }
}

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
            : Task.FromResult<IReadOnlyList<PermissionGrant>>([]);

}

public sealed class PermissionAuthorizationService
{
    private readonly PermissionDefinitionRegistry _definitions;
    private readonly IPermissionGrantProvider _grants;

    public PermissionAuthorizationService(
        PermissionDefinitionRegistry definitions,
        IPermissionGrantProvider grants)
    {
        _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        _grants = grants ?? throw new ArgumentNullException(nameof(grants));
    }

    public async Task<bool> IsGrantedAsync(
        long subjectId,
        long? tenantId,
        string moduleKey,
        PermissionFlags requiredFlags,
        CancellationToken cancellationToken = default)
        => await IsGrantedAsync(subjectId, tenantId, string.Empty, moduleKey, requiredFlags, cancellationToken)
            .ConfigureAwait(false);

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

    public async Task<bool> IsCustomPermissionGrantedAsync(
        long subjectId,
        long? tenantId,
        string moduleKey,
        string permissionName,
        CancellationToken cancellationToken = default)
        => await IsCustomPermissionGrantedAsync(
            subjectId, tenantId, string.Empty, moduleKey, permissionName, cancellationToken).ConfigureAwait(false);

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
        if (permissionName != permissionName.Trim())
            throw new ArgumentException("Custom permission names must have no surrounding whitespace.", nameof(permissionName));

        var moduleDefinitions = _definitions.GetByModule(moduleKey, clientCode);
        if (!moduleDefinitions.Any(definition => (definition.Flags & PermissionFlags.Custom) != 0))
            return false;

        var subjectGrants = await GetModuleGrantsAsync(subjectId, tenantId, clientCode, moduleKey, cancellationToken)
            .ConfigureAwait(false);
        return subjectGrants.Any(grant => grant.CustomPermissions.Contains(permissionName, StringComparer.Ordinal));
    }

    public async Task<bool> IsExtensionPermissionGrantedAsync(
        long subjectId,
        long? tenantId,
        string moduleKey,
        uint requiredExtensionFlags,
        CancellationToken cancellationToken = default)
        => await IsExtensionPermissionGrantedAsync(
            subjectId, tenantId, string.Empty, moduleKey, requiredExtensionFlags, cancellationToken).ConfigureAwait(false);

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
        if (clientCode.Length > 64 || clientCode != clientCode.Trim())
            throw new ArgumentException("Client codes must be trimmed and at most 64 characters.", parameterName);
    }
}