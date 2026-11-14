namespace PubSoft.Authentication.Jwt;


/// <summary>JWKS document payload.</summary>
public sealed class JwtJwksDocument
{
    /// <summary>Published keys.</summary>
    public List<JwtJwksKey> Keys { get; init; } = [];
}
