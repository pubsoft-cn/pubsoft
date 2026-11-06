// README anchors: Cryptography keyring + JwtSigning quick start + AspNetCore signed creds
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.AspNetCore.Authentication;
using PubSoft.Authentication.Jwt;
using PubSoft.Cryptography;

namespace PubSoft.DocsSnippets;

/// <summary>Snippets: keyring, JWT quick start, signed client credentials.</summary>
internal static class SecurityAndIdentitySnippets
{
    // src/core/PubSoft.Cryptography/README.md — ConfigEncryptionKeyRing
    internal static IEncryptionKeyRing Keyring()
    {
        var options = new EncryptionKeyRingOptions
        {
            ActiveKeyId = "k1",
            Keys = new Dictionary<string, string> { ["k1"] = Convert.ToBase64String(new byte[32]) },
        };
        return new ConfigEncryptionKeyRing(options);
    }

    // README.md quick start — JwtSigning round trip
    internal static void JwtRoundTrip()
    {
        using var key = JwtSigning.CreateRsaKey("key-1");
        var token = JwtSigning.CreateAccessToken(key, "issuer", "api",
            new Dictionary<string, string> { ["sub"] = "42" }, TimeSpan.FromMinutes(30));
        _ = JwtSigning.ValidateAccessToken(token, "issuer", "api", key);
    }

    // src/host/PubSoft.AspNetCore/README.md — signed client credentials seam
    internal static void SignedCredentials(IServiceCollection services)
        => services.AddSingleton<IClientSignatureVerifier, DocSignatureVerifier>();

    private sealed class DocSignatureVerifier : IClientSignatureVerifier
    {
        public ValueTask<ClientCredentialVerificationResult> VerifyAsync(
            ClientSignatureRequest request, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ClientCredentialVerificationResult.Ok(request.ClientId));
    }
}
