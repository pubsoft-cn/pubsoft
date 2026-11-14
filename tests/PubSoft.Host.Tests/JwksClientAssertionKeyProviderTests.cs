using Microsoft.Extensions.DependencyInjection;
using PubSoft.Authentication.Jwt;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class JwksClientAssertionKeyProviderTests
{
    [Fact]
    public async Task ResolvesRsaPublicKeyByKidAndVerifiesAssertion()
    {
        using var signer = JwtSigning.CreateRsaKey("kid-1");
        var provider = Provider(_ => Task.FromResult<string?>(Jwks(signer)));

        var material = await provider.GetPublicKeyAsync("svc-caller", "kid-1");

        Assert.NotNull(material);
        Assert.Equal("kid-1", material.KeyId);

        // The JWK-imported public key must actually verify an assertion signed by the private key.
        var assertion = ClientAssertion.Sign(signer, "svc-caller", "svc-target");
        var claims = JwtSigning.ValidateAccessToken(assertion, "svc-caller", "svc-target", material);
        Assert.Equal("svc-caller", claims["sub"]);
    }

    [Fact]
    public async Task UnknownKidAndAmbiguousSetFailClosed()
    {
        using var key1 = JwtSigning.CreateRsaKey("kid-1");
        using var key2 = JwtSigning.CreateRsaKey("kid-2");
        var provider = Provider(_ => Task.FromResult<string?>(Jwks(key1, key2)));

        Assert.Null(await provider.GetPublicKeyAsync("c", "kid-missing"));
        // kid-less assertion against a multi-key set is ambiguous - fail closed.
        Assert.Null(await provider.GetPublicKeyAsync("c", null));
        Assert.NotNull(await provider.GetPublicKeyAsync("c", "kid-2"));
    }

    [Fact]
    public async Task KidLessAssertionResolvesSingleKeySet()
    {
        using var signer = JwtSigning.CreateRsaKey("kid-1");
        var provider = Provider(_ => Task.FromResult<string?>(Jwks(signer)));

        Assert.NotNull(await provider.GetPublicKeyAsync("c", null));
    }

    [Fact]
    public async Task UnknownClientIsNegativelyCached()
    {
        var calls = 0;
        var provider = Provider(_ =>
        {
            calls++;
            return Task.FromResult<string?>(null);
        });

        Assert.Null(await provider.GetPublicKeyAsync("ghost", "kid-1"));
        Assert.Null(await provider.GetPublicKeyAsync("ghost", "kid-1"));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task FetchFailuresAndMalformedJwksFailClosed()
    {
        var provider = Provider(_ => Task.FromException<string?>(new HttpRequestException("down")));
        Assert.Null(await provider.GetPublicKeyAsync("c", "kid-1"));

        var malformed = Provider(_ => Task.FromResult<string?>("not-json"));
        Assert.Null(await malformed.GetPublicKeyAsync("c", "kid-1"));

        var nonRsa = Provider(_ => Task.FromResult<string?>(
            """{"keys":[{"kty":"EC","kid":"e1","crv":"P-256","x":"a","y":"b"}]}"""));
        Assert.Null(await nonRsa.GetPublicKeyAsync("c", "e1"));
    }

    [Fact]
    public async Task PositiveResultIsCachedWithinDuration()
    {
        var calls = 0;
        using var signer = JwtSigning.CreateRsaKey("kid-1");
        var provider = Provider(_ =>
        {
            calls++;
            return Task.FromResult<string?>(Jwks(signer));
        });

        Assert.NotNull(await provider.GetPublicKeyAsync("c", "kid-1"));
        Assert.NotNull(await provider.GetPublicKeyAsync("c", "kid-1"));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void RegistrationWiresProviderAndValidates()
    {
        var services = new ServiceCollection();
        services.AddPubSoftJwksClientAssertionKeyProvider(o => o.FetchJwks = (_, _) => Task.FromResult<string?>(null));
        using var provider = services.BuildServiceProvider();
        Assert.IsType<JwksClientAssertionKeyProvider>(provider.GetRequiredService<IClientAssertionKeyProvider>());

        Assert.Throws<ArgumentNullException>(() => new ServiceCollection()
            .AddPubSoftJwksClientAssertionKeyProvider(_ => { }));
    }

    private static JwksClientAssertionKeyProvider Provider(Func<string, Task<string?>> fetch)
        => new(new JwksClientAssertionKeyOptions { FetchJwks = (clientId, _) => fetch(clientId) });

    private static string Jwks(params RsaSigningKeyMaterial[] materials)
    {
        var keys = string.Join(",", materials.Select(m =>
        {
            var p = m.Rsa.ExportParameters(false);
            return $$"""{"kty":"RSA","kid":"{{m.KeyId}}","use":"sig","alg":"RS256","n":"{{B64(p.Modulus!)}}","e":"{{B64(p.Exponent!)}}"}""";
        }));
        return $$"""{"keys":[{{keys}}]}""";
    }

    private static string B64(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
