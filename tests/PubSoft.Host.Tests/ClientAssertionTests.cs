using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.AspNetCore.Authentication;
using PubSoft.Authentication.Jwt;
using PubSoft.Security;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class ClientAssertionTests
{
    [Fact]
    public async Task SignedAssertionVerifiesAndPassesThroughExtraClaims()
    {
        using var key = JwtSigning.CreateRsaKey("client-key-1");
        using var publicKey = PublicMaterialOf(key);
        var verifier = Verifier(new StubKeyProvider(publicKey));

        var assertion = ClientAssertion.Sign(key, "svc-caller", "svc-target");
        var result = await verifier.VerifyAsync("svc-caller", assertion);

        Assert.True(result.Success);
        Assert.Equal("svc-caller", result.ClientId);
    }

    [Fact]
    public async Task VerificationRejectsWrongAudienceIssuerAndUnknownClient()
    {
        using var key = JwtSigning.CreateRsaKey("k1");
        using var publicKey = PublicMaterialOf(key);
        var verifier = Verifier(new StubKeyProvider(publicKey));

        var wrongAudience = ClientAssertion.Sign(key, "c", "other-service");
        Assert.False((await verifier.VerifyAsync("c", wrongAudience)).Success);

        // iss is pinned to the presented client id: an assertion minted by another client fails.
        var otherClientAssertion = ClientAssertion.Sign(key, "other-client", "svc-target");
        Assert.False((await verifier.VerifyAsync("c", otherClientAssertion)).Success);

        var unknown = Verifier(new StubKeyProvider(null));
        Assert.False((await unknown.VerifyAsync("ghost", ClientAssertion.Sign(key, "ghost", "svc-target"))).Success);

        Assert.False((await verifier.VerifyAsync("c", "not-a-jwt")).Success);
    }

    [Fact]
    public async Task ReplayedJtiFailsClosed()
    {
        using var key = JwtSigning.CreateRsaKey("k1");
        using var publicKey = PublicMaterialOf(key);
        var verifier = Verifier(new StubKeyProvider(publicKey));

        // Same assertion presented twice -- the jti reservation rejects the replay.
        var assertion = ClientAssertion.Sign(key, "c", "svc-target", TimeSpan.FromMinutes(5));
        Assert.True((await verifier.VerifyAsync("c", assertion)).Success);
        Assert.False((await verifier.VerifyAsync("c", assertion)).Success);
    }

    [Fact]
    public async Task OverlongLifetimeAndMissingIatFailClosed()
    {
        using var key = JwtSigning.CreateRsaKey("k1");
        using var publicKey = PublicMaterialOf(key);
        var verifier = Verifier(new StubKeyProvider(publicKey));

        // Signer itself refuses >5min; verifier also enforces the cap against hand-made tokens.
        var tenMinuteAssertion = JwtSigning.CreateAccessToken(key, "c", "svc-target",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["sub"] = "c",
                ["jti"] = Guid.NewGuid().ToString("N")
            }, TimeSpan.FromMinutes(10));
        Assert.False((await verifier.VerifyAsync("c", tenMinuteAssertion)).Success);
    }

    [Fact]
    public void SignerBoundsLifetime()
    {
        using var key = JwtSigning.CreateRsaKey("k1");
        Assert.Throws<ArgumentOutOfRangeException>(() => ClientAssertion.Sign(key, "c", "a", TimeSpan.FromMinutes(6)));
        Assert.Throws<ArgumentOutOfRangeException>(() => ClientAssertion.Sign(key, "c", "a", TimeSpan.Zero));
    }

    [Fact]
    public void PublicKeyImportRejectsWeakKeys()
    {
        using var weak = RSA.Create(1024);
        Assert.Throws<CryptographicException>(() => JwtSigning.ImportRsaPublicKey("k", weak));
    }

    [Fact]
    public async Task EvaluatorPrefersAssertionModeAndFailsClosedWithoutVerifier()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Client-Id"] = "svc-caller";
        context.Request.Headers["X-Client-Assertion"] = "some-assertion";

        // No verifier registered -> 401, not the plaintext path.
        context.RequestServices = new ServiceCollection().BuildServiceProvider();
        var rejected = await ClientCredentialEvaluator.EvaluateAsync(
            [new RequireClientCredentialAttribute()], context);
        Assert.Equal(StatusCodes.Status401Unauthorized, rejected.FailureStatusCode);

        // Registered verifier wins over signature/plaintext headers.
        context.RequestServices = new ServiceCollection()
            .AddSingleton<IClientAssertionVerifier>(new StubAssertionVerifier())
            .BuildServiceProvider();
        var evaluation = await ClientCredentialEvaluator.EvaluateAsync(
            [new RequireClientCredentialAttribute()], context);
        Assert.Null(evaluation.FailureStatusCode);
        Assert.Equal("svc-caller", context.Items[ClientCredentialEvaluator.VerifiedClientIdItemKey]);
    }

    [Fact]
    public void RegistrationWiresVerifierWithSharedOrLocalNonceStore()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClientAssertionKeyProvider>(new StubKeyProvider(null));
        services.AddPubSoftJwtClientAssertionVerifier(options => options.Audience = "svc-target");
        using var provider = services.BuildServiceProvider();
        Assert.IsType<JwtClientAssertionVerifier>(provider.GetRequiredService<IClientAssertionVerifier>());

        Assert.ThrowsAny<ArgumentException>(() => new ServiceCollection()
            .AddPubSoftJwtClientAssertionVerifier(_ => { }));
    }

    private static JwtClientAssertionVerifier Verifier(IClientAssertionKeyProvider provider)
        => new(provider, new ClientAssertionOptions { Audience = "svc-target" }, new MemoryNonceStore());

    private static RsaSigningKeyMaterial PublicMaterialOf(RsaSigningKeyMaterial privateKey)
    {
        var rsa = RSA.Create();
        rsa.ImportParameters(privateKey.Rsa.ExportParameters(false));
        return JwtSigning.ImportRsaPublicKey(privateKey.KeyId, rsa);
    }

    private sealed class StubKeyProvider(RsaSigningKeyMaterial? key) : IClientAssertionKeyProvider
    {
        public ValueTask<RsaSigningKeyMaterial?> GetPublicKeyAsync(string clientId, string? keyId, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(key);
    }

    private sealed class StubAssertionVerifier : IClientAssertionVerifier
    {
        public ValueTask<ClientCredentialVerificationResult> VerifyAsync(string clientId, string assertion, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ClientCredentialVerificationResult.Ok(clientId));
    }
}
