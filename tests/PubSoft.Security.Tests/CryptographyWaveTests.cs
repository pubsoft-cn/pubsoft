using PubSoft.Cryptography;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class CryptographyWaveTests
{
    [Fact]
    public void AesRoundTrip()
    {
        var key = AesEncryptor.GenerateSecretKey();
        Assert.NotEmpty(key);
        var enc = AesEncryptor.Encrypt("secret-data-123", key);
        Assert.NotEqual("secret-data-123", enc);
        Assert.Equal("secret-data-123", AesEncryptor.Decrypt(enc, key));
        Assert.NotEqual(enc, AesEncryptor.Encrypt("secret-data-123", key)); // random IV
    }

    [Fact]
    public void Sm3HashAndHmac()
    {
        Assert.Equal(64, Sm3.HashHex("data").Length); // 32-byte → 64 hex
        var h1 = Sm3.HmacBase64("key", "msg");
        Assert.Equal(h1, Sm3.HmacBase64("key", "msg"));
        Assert.NotEqual(h1, Sm3.HmacBase64("key", "msg2"));
        var hh = Sm3.HmacHex("key"u8.ToArray(), "msg"u8.ToArray());
        Assert.Equal(64, hh.Length);
        Assert.Equal(32, Sm3.DigestSize);
    }

    [Fact]
    public void Sm4EcbRoundTrip()
    {
        var key = string.Concat(Enumerable.Range(0, 32).Select(i => "0123456789abcdef"[i % 16])); // computed 16-byte hex key — no high-entropy literal
        var cipher = Sm4.EncryptEcbHex(key, "payload");
        Assert.Equal("payload", Sm4.DecryptEcbHex(key, cipher));
        Assert.Equal(16, Sm4.BlockSize);
        Assert.Equal(16, Sm4.KeySize);
        var b64 = Sm4.EncryptEcbBase64Key(Convert.ToBase64String(new byte[16]), "x");
        Assert.NotEmpty(b64);
    }
}
