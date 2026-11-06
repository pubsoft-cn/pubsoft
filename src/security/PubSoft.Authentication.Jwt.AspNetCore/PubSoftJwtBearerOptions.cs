using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

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
}
