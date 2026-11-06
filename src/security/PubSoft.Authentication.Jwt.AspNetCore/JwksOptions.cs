using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace PubSoft.Authentication.Jwt.AspNetCore;

public sealed class JwksOptions
{
    public const string SectionName = "AuthServer";
    public string Issuer { get; set; } = "";
    public string JwksUri { get; set; } = "";
    /// <summary>
    /// Expected token audience for the AuthServer scheme. When set, audience is
    /// validated; when empty it is not — configure it whenever the issuer mints
    /// tokens for more than one API.
    /// </summary>
    public string Audience { get; set; } = "";
    public int CacheMinutes { get; set; } = 15;
    /// <summary>
    /// Permits a non-https <see cref="JwksUri"/> outside loopback. Default false —
    /// a plaintext JWKS fetch lets an on-path attacker substitute signing keys.
    /// </summary>
    public bool AllowInsecureJwksUri { get; set; }
}
