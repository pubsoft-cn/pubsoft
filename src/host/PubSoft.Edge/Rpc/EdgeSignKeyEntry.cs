namespace PubSoft.Edge;

/// <summary>
/// One entry of an edge signing-key bundle: the secret key and optional pinned scheme
/// for a single edge client id.
/// </summary>
/// <param name="ClientId">Edge caller identifier (envelope <c>clientId</c>).</param>
/// <param name="SecretKey">Shared signing key for signature schemes (plaintext - the verifier must hold it).</param>
/// <param name="Scheme">
/// Optional scheme pin (e.g. <c>"hmac"</c>, <c>"md5"</c>, <c>"session"</c>, <c>"none"</c>) enforced via
/// <see cref="IEdgeSchemePolicy"/>; null leaves the scheme unconstrained.
/// </param>
public sealed record EdgeSignKeyEntry(string ClientId, string SecretKey, string? Scheme = null);
