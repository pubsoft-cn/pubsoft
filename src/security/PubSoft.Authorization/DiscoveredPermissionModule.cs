using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PubSoft.Authorization;


public sealed record DiscoveredPermissionModule
{
    public required string ModuleKey { get; init; }
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    public byte AvailableFlags { get; init; } = 63;
    public List<string> CustomPermissions { get; init; } = new();
    public int SortOrder { get; init; }
    public List<string> ApiPaths { get; init; } = new();
}
