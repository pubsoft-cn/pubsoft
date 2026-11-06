using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace PubSoft.Authentication.Jwt;


public sealed class JwtJwksKey
{
    public string Kty { get; init; } = "RSA";
    public string Use { get; init; } = "sig";
    public string Kid { get; init; } = string.Empty;
    public string Alg { get; init; } = SecurityAlgorithms.RsaSha256;
    public string N { get; init; } = string.Empty;
    public string E { get; init; } = string.Empty;
}
