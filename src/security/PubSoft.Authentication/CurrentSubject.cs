using System.Globalization;

namespace PubSoft.Authentication;

/// <summary>A subject resolved from a claims snapshot -- the richer sibling of <see cref="AuthenticatedSubject"/>.</summary>
/// <param name="SubjectId">Numeric subject id.</param>
/// <param name="TenantId">Tenant scope.</param>
/// <param name="Name">Display name.</param>
/// <param name="Email">Email.</param>
/// <param name="ClientId">Client id for machine callers.</param>
/// <param name="Roles">Role names.</param>
public sealed record CurrentSubject(
    long SubjectId,
    long? TenantId,
    string? Name,
    string? Email,
    string? ClientId,
    IReadOnlyList<string> Roles);