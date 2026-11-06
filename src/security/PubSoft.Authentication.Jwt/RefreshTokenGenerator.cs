using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace PubSoft.Authentication.Jwt;


/// <summary>Opaque refresh-token generation.</summary>
public static class RefreshTokenGenerator
{
    /// <summary>Creates a URL-safe refresh token.</summary>
    public static string CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return "rt_" + Base64UrlEncoder.Encode(bytes);
    }
}
