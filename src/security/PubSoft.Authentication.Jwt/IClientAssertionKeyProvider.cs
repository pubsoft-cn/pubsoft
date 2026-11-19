namespace PubSoft.Authentication.Jwt;

/// <summary>
/// Resolves a client's public assertion key by client id -- the asymmetric sibling of
/// <see cref="IClientSigningKeyProvider"/>. Implement against the host's client credential
/// registry (cache strongly recommended -- the resolver runs on every assertion request).
/// Implementations return immutable public parameters; verifiers materialize and dispose
/// their own key objects per verification, so no disposable state crosses the boundary.
/// </summary>
public interface IClientAssertionKeyProvider
{
    /// <summary>Returns the public key for <paramref name="clientId"/>, or <c>null</c> when the client is unknown.</summary>
    ValueTask<ClientAssertionPublicKey?> GetPublicKeyAsync(string clientId, string? keyId, CancellationToken cancellationToken = default);
}
