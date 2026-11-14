namespace PubSoft.Authentication.Jwt.AspNetCore;

/// <summary>JWKS endpoint validation settings for the AuthServer scheme.</summary>
public sealed class JwksOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "AuthServer";
    /// <summary>Expected issuer.</summary>
    public string Issuer { get; set; } = "";
    /// <summary>JWKS endpoint (derived from issuer when omitted).</summary>
    public string JwksUri { get; set; } = "";
    /// <summary>
    /// Expected token audience for the AuthServer scheme. When set, audience is
    /// validated; when empty it is not -- configure it whenever the issuer mints
    /// tokens for more than one API.
    /// </summary>
    public string Audience { get; set; } = "";
    /// <summary>JWKS cache TTL.</summary>
    public int CacheMinutes { get; set; } = 15;
    /// <summary>
    /// Permits a non-https <see cref="JwksUri"/> outside loopback. Default false --
    /// a plaintext JWKS fetch lets an on-path attacker substitute signing keys.
    /// </summary>
    public bool AllowInsecureJwksUri { get; set; }
}
