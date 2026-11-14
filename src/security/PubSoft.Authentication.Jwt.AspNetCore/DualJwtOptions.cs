namespace PubSoft.Authentication.Jwt.AspNetCore;

/// <summary>Local-issuer JWT options for the dual-scheme setup.</summary>
public sealed class DualJwtOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "Jwt";
    /// <summary>Expected issuer.</summary>
    public string Issuer { get; set; } = "";
    /// <summary>Expected audience.</summary>
    public string Audience { get; set; } = "";
    /// <summary>Shared machine-key secret.</summary>
    public string SecretKey { get; set; } = "";

    /// <summary>Claim type carrying the client id for per-client signing keys (resolved via <see cref="IClientSigningKeyProvider"/>).</summary>
    public string ClientIdClaimType { get; set; } = "cid";
}
