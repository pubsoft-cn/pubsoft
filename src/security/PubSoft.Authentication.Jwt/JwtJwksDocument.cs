using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace PubSoft.Authentication.Jwt;


public sealed class JwtJwksDocument
{
    public List<JwtJwksKey> Keys { get; init; } = [];
}
