using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using PubSoft.Authentication.Jwt;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class JwtSigningTests
{
    [Fact]
    public void RsaTokenRoundTripsClaimsWithIssuerAudienceAndKeyId()
    {
        using var key = JwtSigning.CreateRsaKey("key-1");
        var token = JwtSigning.CreateAccessToken(key, "issuer", "api", new Dictionary<string, string>
        {
            ["subject_id"] = "42"
        }, TimeSpan.FromMinutes(10));

        var claims = JwtSigning.ValidateAccessToken(token, "issuer", "api", key);

        Assert.Equal("42", claims["subject_id"]);
        Assert.Equal("key-1", new JwtSecurityTokenHandler().ReadJwtToken(token).Header.Kid);
    }

    [Fact]
    public void ValidationPreservesStandardJwtClaimNames()
    {
        using var key = JwtSigning.CreateRsaKey("key-sub");
        var token = JwtSigning.CreateAccessToken(key, "issuer", "api", new Dictionary<string, string>
        {
            [JwtRegisteredClaimNames.Sub] = "user-42"
        }, TimeSpan.FromMinutes(5));

        var claims = JwtSigning.ValidateAccessToken(token, "issuer", "api", key);

        Assert.Equal("user-42", claims[JwtRegisteredClaimNames.Sub]);
    }

    [Theory]
    [InlineData("other-issuer", "api")]
    [InlineData("issuer", "other-api")]
    public void ValidationRejectsIncorrectIssuerOrAudience(string issuer, string audience)
    {
        using var key = JwtSigning.CreateRsaKey("key-issuer");
        var token = JwtSigning.CreateAccessToken(key, "issuer", "api", new Dictionary<string, string>(), TimeSpan.FromMinutes(5));

        Assert.ThrowsAny<SecurityTokenException>(() => JwtSigning.ValidateAccessToken(token, issuer, audience, key));
    }

    [Fact]
    public void ValidationRejectsExpiredTokens()
    {
        using var key = JwtSigning.CreateRsaKey("key-expiry");
        var clock = new FixedTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var token = JwtSigning.CreateAccessToken(key, "issuer", "api", new Dictionary<string, string>(), TimeSpan.FromMinutes(1), clock);
        clock.Advance(TimeSpan.FromMinutes(2));

        Assert.ThrowsAny<SecurityTokenException>(() => JwtSigning.ValidateAccessToken(token, "issuer", "api", key, clock));
    }

    [Fact]
    public void KeyRotationRequiresAnExactConfiguredKeyId()
    {
        using var oldKey = JwtSigning.CreateRsaKey("old");
        using var currentKey = JwtSigning.CreateRsaKey("current");
        var token = JwtSigning.CreateAccessToken(currentKey, "issuer", "api", new Dictionary<string, string>
        {
            ["version"] = "2"
        }, TimeSpan.FromMinutes(5));

        Assert.Equal("2", JwtSigning.ValidateAccessToken(token, "issuer", "api", new[] { oldKey, currentKey })["version"]);
        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(() =>
            JwtSigning.ValidateAccessToken(token, "issuer", "api", new[] { oldKey }));
    }

    [Fact]
    public void ValidationRejectsHs256TokensWithoutAlgorithmDowngrade()
    {
        using var key = JwtSigning.CreateRsaKey("rsa");
        var symmetricKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32)) { KeyId = "rsa" };
        var token = new JwtSecurityToken(
            "issuer",
            "api",
            [new System.Security.Claims.Claim("sub", "42")],
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow.AddMinutes(5),
            new SigningCredentials(symmetricKey, SecurityAlgorithms.HmacSha256));
        token.Header[JwtHeaderParameterNames.Kid] = "rsa";
        var compactToken = new JwtSecurityTokenHandler().WriteToken(token);

        Assert.Throws<SecurityTokenException>(() => JwtSigning.ValidateAccessToken(compactToken, "issuer", "api", key));
    }

    [Fact]
    public void JwksExportsOnlyPublicRsaParametersAndUniqueIds()
    {
        using var key = JwtSigning.CreateRsaKey("public-key");

        var jwks = JwtSigning.ExportJwks([key]);
        var jwk = Assert.Single(jwks.Keys);
        Assert.Equal("RSA", jwk.Kty);
        Assert.Equal("RS256", jwk.Alg);
        Assert.Equal("public-key", jwk.Kid);
        Assert.NotEmpty(jwk.N);
        Assert.NotEmpty(jwk.E);
        Assert.Throws<ArgumentException>(() => JwtSigning.ExportJwks([key, key]));
    }

    [Fact]
    public void PemKeyImportCanSignAndVerify()
    {
        using var generated = JwtSigning.CreateRsaKey("generated");
        var pem = generated.Rsa.ExportRSAPrivateKeyPem();
        using var imported = JwtSigning.ImportFromPem("imported", pem);
        var token = JwtSigning.CreateAccessToken(imported, "issuer", "api", new Dictionary<string, string>
        {
            ["claim"] = "value"
        }, TimeSpan.FromMinutes(5));

        Assert.Equal("value", JwtSigning.ValidateAccessToken(token, "issuer", "api", imported)["claim"]);
    }

    [Fact]
    public void RefreshTokensAreOpaqueRandomValues()
    {
        var first = RefreshTokenGenerator.CreateRefreshToken();
        var second = RefreshTokenGenerator.CreateRefreshToken();

        Assert.StartsWith("rt_", first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void RsaKeyCreationRejectsWeakKeysAndMissingIdentifiers()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JwtSigning.CreateRsaKey("weak", 1024));
        Assert.Throws<ArgumentException>(() => JwtSigning.CreateRsaKey(" "));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }
}
public sealed class SymmetricJwtSigningTests
{
    [Fact]
    public void SymmetricTokenRoundTripsClaimsWithIssuerAudienceAndKeyId()
    {
        using var key = JwtSigning.CreateSymmetricKey("sym-1");
        var token = JwtSigning.CreateAccessToken(key, "issuer", "api", new Dictionary<string, string>
        {
            ["subject_id"] = "42"
        }, TimeSpan.FromMinutes(10));

        var claims = JwtSigning.ValidateAccessToken(token, "issuer", "api", key);

        Assert.Equal("42", claims["subject_id"]);
        Assert.Equal("sym-1", new JwtSecurityTokenHandler().ReadJwtToken(token).Header.Kid);
        Assert.Equal("HS256", new JwtSecurityTokenHandler().ReadJwtToken(token).Header.Alg);
    }

    [Fact]
    public void SymmetricValidationRejectsTokenSignedByDifferentKey()
    {
        using var signer = JwtSigning.CreateSymmetricKey("sym-a");
        using var verifier = JwtSigning.ImportSymmetricKey("sym-a", new string('b', 40));
        var token = JwtSigning.CreateAccessToken(signer, "issuer", "api", new Dictionary<string, string>(), TimeSpan.FromMinutes(5));

        Assert.ThrowsAny<SecurityTokenException>(() => JwtSigning.ValidateAccessToken(token, "issuer", "api", verifier));
    }

    [Fact]
    public void SymmetricKeyCollectionResolvesByKeyId()
    {
        using var signing = JwtSigning.CreateSymmetricKey("sym-live");
        using var other = JwtSigning.ImportSymmetricKey("sym-other", new string('c', 40));
        using var verifier = JwtSigning.ImportSymmetricKey("sym-live", new byte[32]);
        // verifier must share the same bytes as signing — rebuild from the same material
        var token = JwtSigning.CreateAccessToken(signing, "issuer", "api", new Dictionary<string, string>(), TimeSpan.FromMinutes(5));

        var matching = new SymmetricSigningKeyMaterial[] { signing };
        var claims = JwtSigning.ValidateAccessToken(token, "issuer", "api", matching);
        Assert.NotNull(claims);

        var onlyOther = new SymmetricSigningKeyMaterial[] { other };
        Assert.Throws<SecurityTokenSignatureKeyNotFoundException>(
            () => JwtSigning.ValidateAccessToken(token, "issuer", "api", onlyOther));
    }

    [Fact]
    public void RsaValidatorRejectsSymmetricTokenAndViceVersa()
    {
        using var rsa = JwtSigning.CreateRsaKey("rsa-1");
        using var sym = JwtSigning.CreateSymmetricKey("sym-1");
        var hsToken = JwtSigning.CreateAccessToken(sym, "issuer", "api", new Dictionary<string, string>(), TimeSpan.FromMinutes(5));
        var rsToken = JwtSigning.CreateAccessToken(rsa, "issuer", "api", new Dictionary<string, string>(), TimeSpan.FromMinutes(5));

        Assert.Throws<SecurityTokenException>(() => JwtSigning.ValidateAccessToken(hsToken, "issuer", "api", rsa));
        Assert.Throws<SecurityTokenException>(() => JwtSigning.ValidateAccessToken(rsToken, "issuer", "api", sym));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(31)]
    public void SymmetricKeyImportRejectsKeysBelow256Bits(int byteLength)
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => JwtSigning.ImportSymmetricKey("weak", new byte[byteLength]));
}
