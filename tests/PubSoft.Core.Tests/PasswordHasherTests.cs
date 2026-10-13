using System.Security.Cryptography;
using System.Text;
using PubSoft.Security;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class PasswordHasherTests
{
    [Fact]
    public void Hash_CreatesSaltedVerifiableHash_AndRequiresRehashAtLowerWorkFactor()
    {
        var first = PasswordHasher.Hash("correct horse battery staple");
        var second = PasswordHasher.Hash("correct horse battery staple");

        Assert.NotEqual(first, second);
        Assert.True(PasswordHasher.Verify("correct horse battery staple", first));
        Assert.False(PasswordHasher.Verify("wrong password", first));
        Assert.False(PasswordHasher.NeedsRehash(first));

        var lowerCost = PasswordHasher.Hash("password", 100_000);
        Assert.True(PasswordHasher.Verify("password", lowerCost));
        Assert.True(PasswordHasher.NeedsRehash(lowerCost));
    }

    [Fact]
    public void Verify_AcceptsLegacySha256_AndRequestsRehash()
    {
        var legacy = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("legacy password")));

        Assert.True(PasswordHasher.Verify("legacy password", legacy));
        Assert.False(PasswordHasher.Verify("other password", legacy));
        Assert.True(PasswordHasher.NeedsRehash(legacy));
    }

    [Fact]
    public void VerifyAndRehash_UpgradesLegacyHashAfterSuccessfulVerification()
    {
        var legacy = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("legacy password")));

        var result = PasswordHasher.VerifyAndRehash("legacy password", legacy);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.ReplacementHash);
        Assert.StartsWith("pbkdf2$", result.ReplacementHash);
        Assert.False(PasswordHasher.NeedsRehash(result.ReplacementHash));
        Assert.True(PasswordHasher.Verify("legacy password", result.ReplacementHash));
    }

    [Fact]
    public void VerifyAndRehash_UpgradesHashBelowCurrentWorkFactor()
    {
        var lowCost = PasswordHasher.Hash("low-cost password", 100_000);

        var result = PasswordHasher.VerifyAndRehash("low-cost password", lowCost);

        Assert.True(result.Succeeded);
        Assert.NotNull(result.ReplacementHash);
        Assert.NotEqual(lowCost, result.ReplacementHash);
        Assert.False(PasswordHasher.NeedsRehash(result.ReplacementHash));
    }

    [Fact]
    public void VerifyAndRehash_DoesNotReturnReplacementHashForInvalidPassword()
    {
        var legacy = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("legacy password")));

        var result = PasswordHasher.VerifyAndRehash("wrong password", legacy);

        Assert.False(result.Succeeded);
        Assert.Null(result.ReplacementHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("pbkdf2$1$00$00")]
    [InlineData("pbkdf2$600000$invalid$invalid")]
    [InlineData("pbkdf2$600000$00000000000000000000000000000000$00")]
    [InlineData("not-a-password-hash")]
    public void Verify_RejectsMalformedHashesWithoutThrowing(string storedHash)
    {
        Assert.False(PasswordHasher.Verify("password", storedHash));
        Assert.True(PasswordHasher.NeedsRehash(storedHash));
    }

    [Theory]
    [InlineData(99_999)]
    [InlineData(2_000_001)]
    public void Hash_RejectsIterationsOutsideSupportedRange(int iterations)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PasswordHasher.Hash("password", iterations));
    }
}