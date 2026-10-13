using System.Security.Cryptography;
using System.Text;
using PubSoft.Cryptography;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class CredentialEncryptionServiceTests
{
    [Fact]
    public void Encrypt_WritesOnlyCurrentAeadEnvelope()
    {
        var service = CreateService("legacy-key");

        var ciphertext = service.Encrypt("credential");

        Assert.StartsWith("pubsoft:v1:", ciphertext, StringComparison.Ordinal);
        var result = service.Decrypt(ciphertext);
        Assert.Equal("credential", result.Plaintext);
        Assert.False(result.RequiresReEncryption);
        Assert.Null(result.ReplacementCiphertext);
    }

    [Fact]
    public void Decrypt_LegacyCbcReturnsAeadReplacementForExplicitPersistence()
    {
        const string oldKey = "old config key";
        var service = CreateService(oldKey);
        var legacyCiphertext = CreateLegacyCiphertext("legacy secret", oldKey);

        var result = service.Decrypt(legacyCiphertext);

        Assert.Equal("legacy secret", result.Plaintext);
        Assert.True(result.RequiresReEncryption);
        Assert.StartsWith("pubsoft:v1:", result.ReplacementCiphertext, StringComparison.Ordinal);
        Assert.Equal("legacy secret", service.Decrypt(result.ReplacementCiphertext!).Plaintext);
    }

    [Fact]
    public void Constructor_RequiresExplicitLegacyKeyMaterial()
    {
        var encryptor = CreateEncryptor();

        Assert.Throws<ArgumentException>(() => new CredentialEncryptionService(encryptor, " "));
    }

    [Fact]
    public void Decrypt_RejectsUnknownEnvelopeVersionsInsteadOfTryingCbc()
    {
        var service = CreateService("legacy-key");

        Assert.Throws<CryptographicException>(() => service.Decrypt("pubsoft:v2:legacy"));
    }

    [Fact]
    public void Decrypt_RejectsLegacyCiphertextWhenKeyIsWrongOrPayloadMalformed()
    {
        var legacyCiphertext = CreateLegacyCiphertext("legacy secret", "right key");

        Assert.Throws<CryptographicException>(() => CreateService("wrong key").Decrypt(legacyCiphertext));
        Assert.Throws<CryptographicException>(() => CreateService("right key").Decrypt("not-base64"));
    }

    [Fact]
    public void Dispose_ClearsLegacyKeyLifetimeAndPreventsFurtherUse()
    {
        var service = CreateService("legacy-key");
        service.Dispose();

        Assert.Throws<ObjectDisposedException>(() => service.Encrypt("secret"));
        Assert.Throws<ObjectDisposedException>(() => service.Decrypt("pubsoft:v2:unknown"));
    }

    private static CredentialEncryptionService CreateService(string oldKey)
        => new(CreateEncryptor(), oldKey);

    private static AesGcmEncryptor CreateEncryptor()
        => new(new TestKeyRing());

    private static string CreateLegacyCiphertext(string plaintext, string oldKeyMaterial)
    {
        var key = SHA256.HashData(Encoding.UTF8.GetBytes(oldKeyMaterial));
        var iv = RandomNumberGenerator.GetBytes(16);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var encryptor = aes.CreateEncryptor();
            var encrypted = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);
            return Convert.ToBase64String(iv.Concat(encrypted).ToArray());
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(iv);
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    private sealed class TestKeyRing : IEncryptionKeyRing
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
        public string ActiveKeyId => "current";

        public bool TryGetKey(string keyId, out ReadOnlyMemory<byte> key)
        {
            key = keyId == ActiveKeyId ? _key : default;
            return keyId == ActiveKeyId;
        }
    }
}