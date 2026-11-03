using PubSoft.Security;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class HmacSignatureValidatorTests
{
    [Fact]
    public async Task ValidateAsync_AcceptsValidSignatureAndRejectsReplay()
    {
        var now = DateTimeOffset.UtcNow;
        var validator = new HmacSignatureValidator(new MemoryNonceStore(), timeProvider: new FixedTimeProvider(now));
        var timestamp = now.ToUnixTimeSeconds().ToString();
        var signature = HmacSignatureValidator.BuildSignature("secret", "POST", "/resource", timestamp, "nonce-1");

        var first = await validator.ValidateAsync("app", signature, timestamp, "nonce-1", "POST", "/resource", null, null, "secret");
        var second = await validator.ValidateAsync("app", signature, timestamp, "nonce-1", "POST", "/resource", null, null, "secret");

        Assert.True(first.IsValid);
        Assert.Equal("app", first.AppId);
        Assert.Equal("nonce_replayed", second.Error);
    }

    [Fact]
    public async Task ValidateAsync_AtomicallyAllowsOnlyOneConcurrentUseOfNonce()
    {
        var now = DateTimeOffset.UtcNow;
        var validator = new HmacSignatureValidator(new MemoryNonceStore(), timeProvider: new FixedTimeProvider(now));
        var timestamp = now.ToUnixTimeSeconds().ToString();
        var signature = HmacSignatureValidator.BuildSignature("secret", "POST", "/resource", timestamp, "nonce-1");
        var results = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => validator.ValidateAsync(
            "app", signature, timestamp, "nonce-1", "POST", "/resource", null, null, "secret").AsTask()));

        Assert.Single(results, result => result.IsValid);
        Assert.Equal(63, results.Count(result => result.Error == "nonce_replayed"));
    }

    [Fact]
    public async Task InvalidSignatureDoesNotConsumeNonce()
    {
        var now = DateTimeOffset.UtcNow;
        var validator = new HmacSignatureValidator(new MemoryNonceStore(), timeProvider: new FixedTimeProvider(now));
        var timestamp = now.ToUnixTimeSeconds().ToString();
        var validSignature = HmacSignatureValidator.BuildSignature("secret", "GET", "/", timestamp, "nonce");

        var invalid = await validator.ValidateAsync("app", new string('0', 64), timestamp, "nonce", "GET", "/", null, null, "secret");
        var valid = await validator.ValidateAsync("app", validSignature, timestamp, "nonce", "GET", "/", null, null, "secret");

        Assert.Equal("signature_mismatch", invalid.Error);
        Assert.True(valid.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_RejectsMissingMalformedAndExpiredInput()
    {
        var now = DateTimeOffset.UtcNow;
        var validator = new HmacSignatureValidator(new MemoryNonceStore(), timeProvider: new FixedTimeProvider(now));
        var currentTimestamp = now.ToUnixTimeSeconds().ToString();
        var expiredTimestamp = now.AddMinutes(-10).ToUnixTimeSeconds().ToString();

        Assert.Equal("invalid_app_id", (await validator.ValidateAsync(" ", "sig", "1", "n", "GET", "/", null, null, "key")).Error);
        Assert.Equal("invalid_nonce", (await validator.ValidateAsync("app", "sig", "1", " ", "GET", "/", null, null, "key")).Error);
        Assert.Equal("invalid_signature", (await validator.ValidateAsync("app", "bad", currentTimestamp, "n", "GET", "/", null, null, "key")).Error);
        Assert.Equal("timestamp_expired", (await validator.ValidateAsync("app", new string('0', 64), expiredTimestamp, "n", "GET", "/", null, null, "key")).Error);
    }

    [Fact]
    public async Task ValidateAsync_EnforcesTimestampBoundariesAndBodyHash()
    {
        var now = DateTimeOffset.UtcNow;
        var validator = new HmacSignatureValidator(new MemoryNonceStore(), new HmacSignatureOptions { MaxTimestampSkewSeconds = 30 }, new FixedTimeProvider(now));
        var timestamp = now.ToUnixTimeSeconds().ToString();
        var bodyHash = HmacSignatureValidator.ComputeBodyHash("body"u8.ToArray());
        var signature = HmacSignatureValidator.BuildSignature("secret", "POST", "/", timestamp, "nonce", bodyHash: bodyHash);

        Assert.True((await validator.ValidateAsync("app", signature, timestamp, "nonce", "POST", "/", null, bodyHash, "secret")).IsValid);
        Assert.Equal("signature_mismatch", (await validator.ValidateAsync("app", signature, timestamp, "nonce-2", "POST", "/", null, "different", "secret")).Error);
    }

    [Fact]
    public async Task ValidateAsync_BindsQueryStringIntoSignature()
    {
        var now = DateTimeOffset.UtcNow;
        var validator = new HmacSignatureValidator(new MemoryNonceStore(), null, new FixedTimeProvider(now));
        var timestamp = now.ToUnixTimeSeconds().ToString();
        // 签名时带 query — 服务端同一 query 才放行
        var signature = HmacSignatureValidator.BuildSignature("secret", "GET", "/resource", timestamp, "n", queryString: "?a=1");
        Assert.True((await validator.ValidateAsync("app", signature, timestamp, "n", "GET", "/resource", "?a=1", null, "secret")).IsValid);
        Assert.Equal("signature_mismatch",
            (await validator.ValidateAsync("app", signature, timestamp, "n", "GET", "/resource", "?a=2", null, "secret")).Error);
    }

    [Fact]
    public async Task ValidateAsync_RejectsNonceExpiryBoundaryAsExpiredTimestamp()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        const int skewSeconds = 30;
        var validator = new HmacSignatureValidator(
            new MemoryNonceStore(timeProvider: new FixedTimeProvider(now)),
            new HmacSignatureOptions { MaxTimestampSkewSeconds = skewSeconds },
            new FixedTimeProvider(now));
        var timestamp = (now.ToUnixTimeSeconds() - skewSeconds).ToString();
        var signature = HmacSignatureValidator.BuildSignature("secret", "GET", "/", timestamp, "nonce-boundary");

        var result = await validator.ValidateAsync("app", signature, timestamp, "nonce-boundary", "GET", "/", null, null, "secret");

        Assert.False(result.IsValid);
        Assert.Equal("timestamp_expired", result.Error);
    }

    [Fact]
    public void Constructor_RejectsInvalidOptions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HmacSignatureValidator(new MemoryNonceStore(), new HmacSignatureOptions { MaxTimestampSkewSeconds = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HmacSignatureValidator(new MemoryNonceStore(), new HmacSignatureOptions { MaxNonceLength = 0 }));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}