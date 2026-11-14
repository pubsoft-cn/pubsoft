using System.Collections.Frozen;
using System.IdentityModel.Tokens.Jwt;
using PubSoft.Authentication.Jwt;
using PubSoft.Security;

namespace PubSoft.AspNetCore.Authentication;


/// <summary>
/// <see cref="IClientAssertionVerifier"/> validating RS256 client assertions
/// (<c>private_key_jwt</c> shape): <c>iss</c>=<c>sub</c>=client id, exact audience, bounded
/// lifetime, and <c>jti</c> replay rejection through <see cref="INonceStore"/>. Client public keys
/// come from the host-registered <see cref="IClientAssertionKeyProvider"/> -- the registry stores
/// public keys only, so a leaked registry cannot mint assertions.
/// </summary>
/// <remarks>
/// The assertion is a bearer credential -- it does not bind the request body, so TLS is required.
/// Replay rejection is only as strong as the nonce store: use a shared implementation
/// (e.g. Redis) for multi-instance deployments.
/// </remarks>
public sealed class JwtClientAssertionVerifier : IClientAssertionVerifier
{
    private static readonly FrozenSet<string> s_registeredClaims =
        new[] { "iss", "sub", "aud", "exp", "nbf", "iat", "jti" }.ToFrozenSet(StringComparer.Ordinal);

    private readonly IClientAssertionKeyProvider _keyProvider;
    private readonly ClientAssertionOptions _options;
    private readonly INonceStore _nonceStore;

    /// <summary>Creates the verifier.</summary>
    public JwtClientAssertionVerifier(
        IClientAssertionKeyProvider keyProvider,
        ClientAssertionOptions options,
        INonceStore nonceStore)
    {
        _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        _options = options;
        _nonceStore = nonceStore ?? throw new ArgumentNullException(nameof(nonceStore));
    }

    /// <inheritdoc />
    public async ValueTask<ClientCredentialVerificationResult> VerifyAsync(
        string clientId, string assertion, CancellationToken cancellationToken = default)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(assertion))
                return ClientCredentialVerificationResult.Fail();
            var jwt = handler.ReadJwtToken(assertion);

            var keyMaterial = await _keyProvider.GetPublicKeyAsync(clientId, jwt.Header.Kid, cancellationToken).ConfigureAwait(false);
            if (keyMaterial is null)
                return ClientCredentialVerificationResult.Fail();

            // Issuer pinned to the claimed client id -- a token minted by another client never verifies.
            var claims = JwtSigning.ValidateAccessToken(
                assertion, clientId, _options.Audience!, keyMaterial, clockSkew: _options.ClockSkew);

            if (!claims.TryGetValue(JwtRegisteredClaimNames.Sub, out var subject)
                || !string.Equals(subject, clientId, StringComparison.Ordinal))
                return ClientCredentialVerificationResult.Fail();
            if (!claims.TryGetValue(JwtRegisteredClaimNames.Jti, out var jti)
                || string.IsNullOrWhiteSpace(jti) || jti.Length > 128)
                return ClientCredentialVerificationResult.Fail();
            // Bounded issuance window: accept iat or nbf as the anchor, then enforce exp - issued.
            long? issued = null;
            if (claims.TryGetValue(JwtRegisteredClaimNames.Iat, out var iatRaw) && long.TryParse(iatRaw, out var iat))
                issued = iat;
            if (issued is null
                && claims.TryGetValue(JwtRegisteredClaimNames.Nbf, out var nbfRaw)
                && long.TryParse(nbfRaw, out var nbf))
                issued = nbf;
            if (issued is null
                || !claims.TryGetValue(JwtRegisteredClaimNames.Exp, out var expRaw)
                || !long.TryParse(expRaw, out var exp)
                || exp - issued.Value > _options.MaxAssertionLifetime.TotalSeconds)
                return ClientCredentialVerificationResult.Fail();

            // Replay rejection: the jti may not be reused within the assertion's own validity window.
            var jtiExpiry = DateTimeOffset.FromUnixTimeSeconds(exp);
            if (!await _nonceStore.TryUseAsync($"client-assertion:{clientId}", jti, jtiExpiry, cancellationToken).ConfigureAwait(false))
                return ClientCredentialVerificationResult.Fail();

            var extraClaims = claims
                .Where(pair => !s_registeredClaims.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            return ClientCredentialVerificationResult.Ok(
                clientId, claims: extraClaims.Count > 0 ? extraClaims : null);
        }
        catch (Exception)
        {
            return ClientCredentialVerificationResult.Fail();
        }
    }
}
