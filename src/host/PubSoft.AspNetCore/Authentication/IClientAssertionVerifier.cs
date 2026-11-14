namespace PubSoft.AspNetCore.Authentication;

/// <summary>
/// Verifies a bearer client assertion carried in the assertion header (e.g. an RS256 JWT).
/// The host supplies the verifier -- typically <see cref="JwtClientAssertionVerifier"/> over an
/// <c>IClientAssertionKeyProvider</c>. Fail-closed: unverified requests never reach the endpoint.
/// </summary>
public interface IClientAssertionVerifier
{
    /// <summary>Verifies the assertion for <paramref name="clientId"/>; returns the credential result.</summary>
    ValueTask<ClientCredentialVerificationResult> VerifyAsync(
        string clientId, string assertion, CancellationToken cancellationToken = default);
}
