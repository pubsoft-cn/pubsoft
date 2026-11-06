using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace PubSoft.Authentication.Jwt;


/// <summary>JWKS document payload.</summary>
public sealed class JwtJwksDocument
{
    /// <summary>Published keys.</summary>
    public List<JwtJwksKey> Keys { get; init; } = [];
}
