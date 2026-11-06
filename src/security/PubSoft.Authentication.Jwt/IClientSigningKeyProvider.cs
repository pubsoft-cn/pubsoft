namespace PubSoft.Authentication.Jwt;

/// <summary>
/// Resolves a per-client symmetric signing key by client id. Implement against the host's
/// client credential registry (cache strongly recommended -- the resolver runs on every
/// authenticated request carrying a client-id claim).
/// </summary>
public interface IClientSigningKeyProvider
{
    /// <summary>Returns the signing key for <paramref name="clientId"/>, or <c>null</c> when the client is unknown.</summary>
    ValueTask<SymmetricSigningKeyMaterial?> GetSigningKeyAsync(string clientId, CancellationToken cancellationToken = default);
}
