namespace PubSoft.Authorization;


/// <summary>One grantable permission: module + flag, with optional discovery metadata.</summary>
public sealed record PermissionDefinition
{
    /// <summary>Client/application code the definition belongs to (empty = all clients).</summary>
    public string ClientCode { get; init; } = string.Empty;
    /// <summary>Module key the permission belongs to.</summary>
    public required string ModuleKey { get; init; }
    /// <summary>The flag bit(s) the definition covers.</summary>
    public required PermissionFlags Flags { get; init; }
    /// <summary>Human-readable name for admin UI.</summary>
    public required string DisplayName { get; init; }
    /// <summary>Optional description.</summary>
    public string? Description { get; init; }
    /// <summary>API routes the permission covers.</summary>
    public IReadOnlyList<string> ApiPaths { get; init; } = Array.Empty<string>();
    /// <summary>Frontend routes gated by the permission.</summary>
    public IReadOnlyList<string> FrontendRoutes { get; init; } = Array.Empty<string>();
    /// <summary>Whether this is an extended (non-flag) permission.</summary>
    public bool IsExtPermission { get; init; }
    /// <summary>Extended flag bit when <see cref="IsExtPermission"/>.</summary>
    public uint ExtFlag { get; init; }
}
