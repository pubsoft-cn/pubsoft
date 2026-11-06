using System.Text;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PubSoft.Cryptography;

namespace PubSoft.EntityFrameworkCore;

public static class EncryptionModelBuilderExtensions
{
    /// <summary>
    /// Stores the string property as AEAD ciphertext via <see cref="CredentialEncryptionService"/>.
    /// Nulls pass through unconverted. Bind <paramref name="additionalAuthenticatedData"/> to a
    /// stable column identity -- ciphertext cannot be transplanted to another column.
    /// </summary>
    /// <remarks>
    /// Reads return plaintext regardless of whether the stored value is GCM ciphertext or legacy
    /// CBC (<see cref="CredentialEncryptionService"/> migrates on read); the converter cannot
    /// persist the replacement ciphertext, so finish the legacy migration with a one-off rewrite
    /// pass before retiring the legacy key.
    /// </remarks>
    public static PropertyBuilder<string?> HasPubSoftEncryption(
        this PropertyBuilder<string?> property,
        CredentialEncryptionService encryption,
        string? additionalAuthenticatedData = null)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(encryption);
        var aad = additionalAuthenticatedData is null ? [] : Encoding.UTF8.GetBytes(additionalAuthenticatedData);
        return property.HasConversion(
            plaintext => encryption.Encrypt(plaintext!, aad),
            ciphertext => encryption.Decrypt(ciphertext, aad).Plaintext);
    }
}
