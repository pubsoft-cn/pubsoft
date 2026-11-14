using System.IdentityModel.Tokens.Jwt;

namespace PubSoft.Authentication.Jwt.AspNetCore;

/// <summary>JWT bearer validation settings (JWKS or symmetric).</summary>
public sealed class PubSoftJwtBearerOptions
{
    /// <summary>Expected issuer.</summary>
    public string Issuer { get; set; } = string.Empty;
    /// <summary>Expected audience.</summary>
    public string Audience { get; set; } = string.Empty;
    /// <summary>Inline JWKS payload.</summary>
    public string JwksJson { get; set; } = string.Empty;
    /// <summary>Discovery metadata URL.</summary>
    public string MetadataAddress { get; set; } = string.Empty;
    /// <summary>Symmetric validation key when used.</summary>
    public byte[]? SymmetricKey { get; set; }
    /// <summary>Key id for the symmetric key.</summary>
    public string SymmetricKeyId { get; set; } = string.Empty;
    /// <summary>Whether metadata must be fetched over https.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;
    /// <summary>Metadata fetch timeout.</summary>
    public TimeSpan BackchannelTimeout { get; set; } = TimeSpan.FromSeconds(15);
    /// <summary>Allowed clock skew.</summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(5);
    /// <summary>Claim mapped to identity name.</summary>
    public string NameClaimType { get; set; } = JwtRegisteredClaimNames.Sub;
    /// <summary>Claim mapped to roles.</summary>
    public string RoleClaimType { get; set; } = "role";
    /// <summary>
    /// Opt-in revocation check: after signature validation, look up SHA-256 of the raw token in
    /// the registered <c>ITokenDenylist</c> (PubSoft.Security). Requires a denylist registered
    /// before this call - fails closed at registration and per-request when unavailable.
    /// </summary>
    public bool CheckDenylist { get; set; }
}
