namespace PubSoft.AspNetCore.Authentication;

/// <summary>
/// Verifies a signed machine credential. Host supplies the protocol -- the evaluator only
/// detects signature headers and hands over the request material. Implementations may use
/// <see cref="PubSoft.Security.HmacSignatureValidator"/> with a matching profile, or a custom
/// scheme entirely. Fail-closed: unverified requests never reach the endpoint.
/// </summary>
public interface IClientSignatureVerifier
{
    /// <summary>Verifies the signed request; returns the credential result (fail-closed).</summary>
    ValueTask<ClientCredentialVerificationResult> VerifyAsync(
        ClientSignatureRequest request, CancellationToken cancellationToken = default);
}
