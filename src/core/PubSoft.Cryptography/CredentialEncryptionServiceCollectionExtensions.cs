using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PubSoft.Cryptography;

/// <summary>Standard credential-encryption registration.</summary>
public static class CredentialEncryptionServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="ConfigEncryptionKeyRing"/>, <see cref="AesGcmEncryptor"/> and
    /// <see cref="CredentialEncryptionService"/> from a configuration section:
    /// <c>Encryption:Keys</c> (keyId =&gt; base64 key), <c>Encryption:ActiveKeyId</c> and
    /// <c>Encryption:LegacyKeyMaterial</c> (pre-ring ciphertext fallback).
    /// </summary>
    public static IServiceCollection AddPubSoftCredentialEncryption(
        this IServiceCollection services, IConfiguration configuration, string sectionName = "Encryption")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(sectionName);
        var activeKeyId = section["ActiveKeyId"];
        ArgumentException.ThrowIfNullOrWhiteSpace(activeKeyId);

        var keys = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var child in section.GetSection("Keys").GetChildren())
            if (child.Value is { Length: > 0 })
                keys[child.Key] = child.Value;
        var legacyKeyMaterial = section["LegacyKeyMaterial"];
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyKeyMaterial);

        var ring = new ConfigEncryptionKeyRing(new EncryptionKeyRingOptions
        {
            ActiveKeyId = activeKeyId,
            Keys = keys
        });
        services.TryAddSingleton<IEncryptionKeyRing>(ring);
        services.TryAddSingleton(new AesGcmEncryptor(ring));
        services.TryAddSingleton(sp => new CredentialEncryptionService(
            sp.GetRequiredService<AesGcmEncryptor>(), legacyKeyMaterial));
        return services;
    }
}
