namespace PubSoft.Cryptography;

/// <summary>Result of a decrypt: the plaintext plus re-encryption guidance when the value used the legacy key.</summary>
/// <param name="Plaintext">The decrypted secret.</param>
/// <param name="RequiresReEncryption">Whether the value should be re-encrypted under the active key.</param>
/// <param name="ReplacementCiphertext">A ready-to-persist ciphertext under the active key, when re-encryption is required.</param>
public sealed record DecryptedCredential(
    string Plaintext,
    bool RequiresReEncryption,
    string? ReplacementCiphertext);
