using System.Security.Cryptography;
using PubSoft.Cryptography;

var keyRing = new SmokeKeyRing();
var encryptor = new AesGcmEncryptor(keyRing);
const string plaintext = "cryptography-consumer";
var envelope = encryptor.Encrypt(plaintext);
if (!envelope.StartsWith("pubsoft:v1:consumer-key:", StringComparison.Ordinal) ||
    encryptor.Decrypt(envelope) != plaintext)
    throw new InvalidOperationException("AES-GCM round-trip consumer check failed.");

var parts = envelope.Split(':');
var ciphertext = Convert.FromBase64String(parts[4]);
ciphertext[0] ^= 0x01;
parts[4] = Convert.ToBase64String(ciphertext);
CryptographicOperations.ZeroMemory(ciphertext);
var tamperedEnvelope = string.Join(':', parts);
try
{
    encryptor.Decrypt(tamperedEnvelope);
    throw new InvalidOperationException("AES-GCM tampering consumer check failed.");
}
catch (CryptographicException)
{
}

Console.WriteLine("Isolated PubSoft.Cryptography consumer smoke check passed.");

sealed class SmokeKeyRing : IEncryptionKeyRing
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public string ActiveKeyId => "consumer-key";

    public bool TryGetKey(string keyId, out ReadOnlyMemory<byte> key)
    {
        key = string.Equals(keyId, ActiveKeyId, StringComparison.Ordinal) ? _key : default;
        return keyId == ActiveKeyId;
    }
}