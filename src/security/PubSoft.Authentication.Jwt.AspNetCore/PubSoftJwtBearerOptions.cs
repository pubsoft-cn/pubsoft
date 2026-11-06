using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace PubSoft.Authentication.Jwt.AspNetCore;

public sealed class PubSoftJwtBearerOptions
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string JwksJson { get; set; } = string.Empty;
    public string MetadataAddress { get; set; } = string.Empty;
    public byte[]? SymmetricKey { get; set; }
    public string SymmetricKeyId { get; set; } = string.Empty;
    public bool RequireHttpsMetadata { get; set; } = true;
    public TimeSpan BackchannelTimeout { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(5);
    public string NameClaimType { get; set; } = JwtRegisteredClaimNames.Sub;
    public string RoleClaimType { get; set; } = "role";
}
