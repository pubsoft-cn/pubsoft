namespace PubSoft.Edge;


/// <summary>
/// Edge call context -- authenticated session data plus the HttpContext.
/// </summary>
public sealed record EdgeContext
{
    /// <summary>HttpContext (null for non-HTTP invocations).</summary>
    public Microsoft.AspNetCore.Http.HttpContext? HttpContext { get; init; }

    /// <summary>Scheme used by this request.</summary>
    public EdgeAuthScheme Scheme { get; init; } = EdgeAuthScheme.None;

    /// <summary>Registered client system identifier.</summary>
    public string ClientId { get; init; } = string.Empty;

    /// <summary>Client-supplied correlation id, when present.</summary>
    public string? RequestId { get; init; }

    /// <summary>Credential verified flag.</summary>
    public bool IsVerified { get; init; }

    /// <summary>Session data (Session scheme; contains the backend token and user info).</summary>
    public EdgeSession? Session { get; init; }

    /// <summary>Current user id (Session scheme).</summary>
    public string? UserId => Session?.UserId;

    /// <summary>Current member id (Session scheme).</summary>
    public string? MemberId => Session?.MemberId;

    /// <summary>Current open id (Session scheme).</summary>
    public string? OpenId => Session?.OpenId;

    /// <summary>Current tenant id.</summary>
    public string? TenantId => Session?.TenantId;

    /// <summary>Session roles.</summary>
    public string[] Roles => Session?.Roles ?? [];

    /// <summary>Backend token held by the session (never exposed to the caller).</summary>
    public string? Token => Session?.Token;
}
