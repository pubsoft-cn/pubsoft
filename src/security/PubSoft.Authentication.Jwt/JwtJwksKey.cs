using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace PubSoft.Authentication.Jwt;


/// <summary>One JWKS key entry.</summary>
public sealed class JwtJwksKey
{
    /// <summary>Key type.</summary>
    public string Kty { get; init; } = "RSA";
    /// <summary>Intended use.</summary>
    public string Use { get; init; } = "sig";
    /// <summary>Key id.</summary>
    public string Kid { get; init; } = string.Empty;
    /// <summary>Algorithm.</summary>
    public string Alg { get; init; } = SecurityAlgorithms.RsaSha256;
    /// <summary>RSA modulus (base64url).</summary>
    public string N { get; init; } = string.Empty;
    /// <summary>RSA exponent (base64url).</summary>
    public string E { get; init; } = string.Empty;
}
