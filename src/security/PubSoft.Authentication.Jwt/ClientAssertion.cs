using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace PubSoft.Authentication.Jwt;

/// <summary>
/// Client-assertion signing (private_key_jwt shape): an RS256 JWT whose issuer and subject are the
/// client id, bound to an explicit audience, carrying a unique <c>jti</c> for replay rejection.
/// </summary>
public static class ClientAssertion
{
    /// <summary>Maximum assertion lifetime the signer will emit.</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Signs an RS256 assertion: <c>iss</c>=<c>sub</c>=<paramref name="clientId"/>,
    /// <c>aud</c>=<paramref name="audience"/>, unique <c>jti</c>. Lifetime defaults to two minutes
    /// and is bounded by <see cref="MaxLifetime"/>.
    /// </summary>
    public static string Sign(
        RsaSigningKeyMaterial keyMaterial,
        string clientId,
        string audience,
        TimeSpan? lifetime = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(keyMaterial);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        var effectiveLifetime = lifetime ?? TimeSpan.FromMinutes(2);
        if (effectiveLifetime <= TimeSpan.Zero || effectiveLifetime > MaxLifetime)
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Client assertion lifetime must be positive and no longer than five minutes.");

        return JwtSigning.CreateAccessToken(keyMaterial, clientId, audience,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [JwtRegisteredClaimNames.Sub] = clientId,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N")
            },
            effectiveLifetime, timeProvider);
    }
}
