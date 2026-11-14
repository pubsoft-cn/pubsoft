namespace PubSoft.Authentication.Jwt;

/// <summary>
/// Resolves a client's public assertion key by client id -- the asymmetric sibling of
/// <see cref="IClientSigningKeyProvider"/>. Implement against the host's client credential
/// registry (cache strongly recommended -- the resolver runs on every assertion request).
/// Implementations return public parameters only and own the returned material's lifetime;
/// consumers must not dispose it.
/// </summary>
public interface IClientAssertionKeyProvider
{
    /// <summary>Returns the public key material for <paramref name="clientId"/>, or <c>null</c> when the client is unknown.</summary>
    ValueTask<RsaSigningKeyMaterial?> GetPublicKeyAsync(string clientId, string? keyId, CancellationToken cancellationToken = default);
}
