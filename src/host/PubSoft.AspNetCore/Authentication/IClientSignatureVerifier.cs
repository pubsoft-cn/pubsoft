using Microsoft.Extensions.Primitives;

namespace PubSoft.AspNetCore.Authentication;

/// <summary>
/// Complete request material for a signed machine credential. The verifier owns the wire
/// protocol -- canonicalization, encoding, timestamp unit, replay policy -- so the raw body is
/// exposed as a reader rather than a pre-computed digest (hash shape is part of the protocol).
/// </summary>
public sealed record ClientSignatureRequest(
    string ClientId,
    string Signature,
    string Timestamp,
    string Nonce,
    string Method,
    string Path,
    string? QueryString,
    Func<CancellationToken, ValueTask<byte[]>> ReadBody);

/// <summary>
/// Verifies a signed machine credential. Host supplies the protocol -- the evaluator only
/// detects signature headers and hands over the request material. Implementations may use
/// <see cref="PubSoft.Security.HmacSignatureValidator"/> with a matching profile, or a custom
/// scheme entirely. Fail-closed: unverified requests never reach the endpoint.
/// </summary>
public interface IClientSignatureVerifier
{
    ValueTask<ClientCredentialVerificationResult> VerifyAsync(
        ClientSignatureRequest request, CancellationToken cancellationToken = default);
}
