using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PubSoft.Authorization;


/// <summary>A permission module as discovered at runtime.</summary>
public sealed record DiscoveredPermissionModule
{
    /// <summary>The module key.</summary>
    public required string ModuleKey { get; init; }
    /// <summary>Human-readable name.</summary>
    public required string DisplayName { get; init; }
    /// <summary>Optional description.</summary>
    public string? Description { get; init; }
    /// <summary>Bitmask of flags the module supports.</summary>
    public byte AvailableFlags { get; init; } = 63;
    /// <summary>Named permissions outside the bitmask space.</summary>
    public List<string> CustomPermissions { get; init; } = new();
    /// <summary>Display ordering.</summary>
    public int SortOrder { get; init; }
    /// <summary>API routes covered.</summary>
    public List<string> ApiPaths { get; init; } = new();
}
