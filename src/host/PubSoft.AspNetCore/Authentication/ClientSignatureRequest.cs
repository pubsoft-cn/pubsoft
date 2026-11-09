using Microsoft.Extensions.Primitives;

namespace PubSoft.AspNetCore.Authentication;

/// <summary>
/// Complete request material for a signed machine credential. The verifier owns the wire
/// protocol -- canonicalization, encoding, timestamp unit, replay policy -- so the raw body is
/// exposed as a reader rather than a pre-computed digest (hash shape is part of the protocol).
/// </summary>
/// <param name="ClientId">Client identifier.</param>
/// <param name="Signature">Request signature.</param>
/// <param name="Timestamp">Request timestamp.</param>
/// <param name="Nonce">Replay nonce.</param>
/// <param name="Method">HTTP method.</param>
/// <param name="Path">Request path.</param>
/// <param name="QueryString">Raw query string.</param>
/// <param name="ReadBody">Lazy raw-body reader (verifier owns hashing).</param>
public sealed record ClientSignatureRequest(
    string ClientId,
    string Signature,
    string Timestamp,
    string Nonce,
    string Method,
    string Path,
    string? QueryString,
    Func<CancellationToken, ValueTask<byte[]>> ReadBody);

