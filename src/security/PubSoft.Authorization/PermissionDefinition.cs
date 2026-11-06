using PubSoft.Core;
namespace PubSoft.Authorization;


public sealed record PermissionDefinition
{
    public string ClientCode { get; init; } = string.Empty;
    public required string ModuleKey { get; init; }
    public required PermissionFlags Flags { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public IReadOnlyList<string> ApiPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> FrontendRoutes { get; init; } = Array.Empty<string>();
    public bool IsExtPermission { get; init; }
    public uint ExtFlag { get; init; }
}
