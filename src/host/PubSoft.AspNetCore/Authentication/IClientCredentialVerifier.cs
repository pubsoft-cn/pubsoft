namespace PubSoft.AspNetCore.Authentication;

/// <summary>
/// Verifies a machine (service-to-service) credential pair. The host supplies the registry
/// (e.g. a hashed-secret store); the credential format and lookup are entirely host-defined.
/// </summary>
public interface IClientCredentialVerifier
{
    /// <summary>Verifies the client id + secret pair.</summary>
    Task<ClientCredentialVerificationResult> VerifyAsync(
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default);
}
