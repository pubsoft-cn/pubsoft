using System.Security.Cryptography;
using PubSoft.Cryptography;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class AesGcmEncryptorTests
{
    [Fact]
    public void Encrypt_UsesVersionedEnvelope_AndRoundTripsIncludingEmptyText()
    {
        var encryptor = new AesGcmEncryptor(new TestKeyRing("key-1", ("key-1", RandomNumberGenerator.GetBytes(32))));
        var envelope = encryptor.Encrypt("sensitive credential");

        Assert.StartsWith("pubsoft:v1:key-1:", envelope, StringComparison.Ordinal);
        Assert.Equal("sensitive credential", encryptor.Decrypt(envelope));
        Assert.Equal(string.Empty, encryptor.Decrypt(encryptor.Encrypt(string.Empty)));
    }

    [Fact]
    public void Encrypt_UsesFreshNonceForEachValue()
    {
        var encryptor = new AesGcmEncryptor(new TestKeyRing("key-1", ("key-1", RandomNumberGenerator.GetBytes(32))));

        Assert.NotEqual(encryptor.Encrypt("same"), encryptor.Encrypt("same"));
    }

    [Fact]
    public void Decrypt_SupportsKeyRotationWhenOldKeyRemainsInRing()
    {
        var oldKey = RandomNumberGenerator.GetBytes(32);
        var newKey = RandomNumberGenerator.GetBytes(32);
        var oldEncryptor = new AesGcmEncryptor(new TestKeyRing("old", ("old", oldKey)));
        var oldEnvelope = oldEncryptor.Encrypt("stored before rotation");
        var rotatedEncryptor = new AesGcmEncryptor(new TestKeyRing("new", ("old", oldKey), ("new", newKey)));

        Assert.Equal("stored before rotation", rotatedEncryptor.Decrypt(oldEnvelope));
        Assert.StartsWith("pubsoft:v1:new:", rotatedEncryptor.Encrypt("new value"), StringComparison.Ordinal);
    }

    [Fact]
    public void Decrypt_RejectsTamperedCiphertextAndAuthenticationTag()
    {
        var encryptor = new AesGcmEncryptor(new TestKeyRing("key-1", ("key-1", RandomNumberGenerator.GetBytes(32))));
        var parts = encryptor.Encrypt("secret").Split(':');
        parts[4] = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5, 6 });
        Assert.Throws<PubSoft.Cryptography.PubSoftDecryptionException>(() => encryptor.Decrypt(string.Join(':', parts)));

        parts = encryptor.Encrypt("secret").Split(':');
        var tag = Convert.FromBase64String(parts[5]);
        tag[0] ^= 0x01;
        parts[5] = Convert.ToBase64String(tag);
        Assert.Throws<PubSoft.Cryptography.PubSoftDecryptionException>(() => encryptor.Decrypt(string.Join(':', parts)));
    }

    [Fact]
    public void Encrypt_BindsOptionalAssociatedDataToCiphertext()
    {
        var encryptor = new AesGcmEncryptor(new TestKeyRing("key-1", ("key-1", RandomNumberGenerator.GetBytes(32))));
        var envelope = encryptor.Encrypt("secret", "tenant-1:credential-7"u8);

        Assert.Equal("secret", encryptor.Decrypt(envelope, "tenant-1:credential-7"u8));
        Assert.Throws<PubSoft.Cryptography.PubSoftDecryptionException>(() => encryptor.Decrypt(envelope, "tenant-2:credential-7"u8));
    }

    [Theory]
    [InlineData("")]
    [InlineData("pubsoft:v2:key-1:AA==:AA==:AA==")]
    [InlineData("pubsoft:v1:key-1:bad:AA==:AA==")]
    [InlineData("pubsoft:v1:bad:key:AA==:AA==")]
    public void Decrypt_RejectsMalformedOrUnsupportedEnvelopes(string envelope)
    {
        var encryptor = new AesGcmEncryptor(new TestKeyRing("key-1", ("key-1", RandomNumberGenerator.GetBytes(32))));

        Assert.Throws<PubSoft.Cryptography.PubSoftDecryptionException>(() => encryptor.Decrypt(envelope));
    }

    [Fact]
    public void Decrypt_RejectsUnknownKey_AndEncryptionRejectsInvalidKeyMaterial()
    {
        var encryptor = new AesGcmEncryptor(new TestKeyRing("missing", ("other", RandomNumberGenerator.GetBytes(32))));

        Assert.Throws<PubSoft.Cryptography.PubSoftDecryptionException>(() => encryptor.Encrypt("secret"));
        Assert.Throws<PubSoft.Cryptography.PubSoftDecryptionException>(() => encryptor.Decrypt("pubsoft:v1:unknown:AAAAAAAAAAAAAAAA:AAAA:AAAAAAAAAAAAAAAAAAAAAA=="));
        Assert.Throws<PubSoft.Cryptography.PubSoftDecryptionException>(() => new AesGcmEncryptor(new TestKeyRing("short", ("short", new byte[16]))).Encrypt("secret"));
    }

    private sealed class TestKeyRing(string activeKeyId, params (string Id, byte[] Key)[] keys) : IEncryptionKeyRing
    {
        private readonly IReadOnlyDictionary<string, byte[]> _keys = keys.ToDictionary(item => item.Id, item => item.Key, StringComparer.Ordinal);

        public string ActiveKeyId { get; } = activeKeyId;

        public bool TryGetKey(string keyId, out ReadOnlyMemory<byte> key)
        {
            if (_keys.TryGetValue(keyId, out var value))
            {
                key = value;
                return true;
            }

            key = default;
            return false;
        }
    }
}