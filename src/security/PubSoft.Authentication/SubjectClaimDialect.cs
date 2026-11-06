using System.Globalization;

namespace PubSoft.Authentication;

/// <summary>
/// Claim-type names mapping a token's claims onto <see cref="CurrentSubject"/>. Dialect is
/// deployment configuration -- hosts pick their claim names; the library owns only the shape.
/// </summary>
public sealed class SubjectClaimDialect
{
    /// <summary>Claim type carrying the numeric subject id. Required.</summary>
    public required string SubjectId { get; set; }
    /// <summary>Claim carrying the tenant id.</summary>
    public string? TenantId { get; set; }
    /// <summary>Claim carrying the display name.</summary>
    public string? Name { get; set; }
    /// <summary>Claim carrying the email.</summary>
    public string? Email { get; set; }
    /// <summary>Claim carrying the client id.</summary>
    public string? ClientId { get; set; }
    /// <summary>Multi-valued role claim type.</summary>
    public string? Roles { get; set; }
}