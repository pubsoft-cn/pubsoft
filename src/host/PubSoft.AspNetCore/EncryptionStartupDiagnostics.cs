using Microsoft.Extensions.DependencyInjection;
using PubSoft.Cryptography;
using PubSoft.Monitoring;

namespace PubSoft.AspNetCore;

/// <summary>Startup posture for credential encryption: proves the configured key ring actually
/// works. A roundtrip alone only proves self-consistency - a drifted key still roundtrips.
/// Provide <see cref="EncryptionDiagnosticsOptions.ProbeCiphertext"/> (a ciphertext produced under the production key) to
/// catch "key configured but cannot decrypt existing data" at startup instead of first request.</summary>
public sealed class EncryptionStartupDiagnostics : IStartupDiagnostics
{
    private readonly IServiceProvider _services;
    private readonly EncryptionDiagnosticsOptions _options;

    /// <summary>Creates the provider.</summary>
    public EncryptionStartupDiagnostics(IServiceProvider services, EncryptionDiagnosticsOptions? options = null)
    {
        _services = services;
        _options = options ?? new EncryptionDiagnosticsOptions();
    }

    /// <inheritdoc />
    public IEnumerable<StartupDiagnosticsEntry> Collect()
    {
        var service = _services.GetService<CredentialEncryptionService>();
        if (service is null)
        {
            yield return new StartupDiagnosticsEntry("credential-encryption",
                StartupDiagnosticsStatus.Warning,
                "no CredentialEncryptionService registered - encryption posture not verifiable");
            yield break;
        }

        var roundtrip = ProbeRoundtrip(service);
        yield return roundtrip;
        if (roundtrip.Status == StartupDiagnosticsStatus.Error)
            yield break;

        if (_options.ProbeCiphertext is { Length: > 0 })
            yield return ProbeExisting(service, _options.ProbeCiphertext);
    }

    private static StartupDiagnosticsEntry ProbeRoundtrip(CredentialEncryptionService service)
    {
        try
        {
            _ = service.Decrypt(service.Encrypt("pubsoft-probe"));
            return new StartupDiagnosticsEntry("credential-encryption-roundtrip",
                StartupDiagnosticsStatus.Ok, "key ring roundtrip probe passed");
        }
        catch (PubSoftDecryptionException ex)
        {
            return new StartupDiagnosticsEntry("credential-encryption-roundtrip",
                StartupDiagnosticsStatus.Error,
                $"key ring roundtrip failed ({ex.Reason}) - {ex.Message}");
        }
        catch (Exception ex)
        {
            return new StartupDiagnosticsEntry("credential-encryption-roundtrip",
                StartupDiagnosticsStatus.Error, $"key ring roundtrip failed - {ex.Message}");
        }
    }

    private static StartupDiagnosticsEntry ProbeExisting(CredentialEncryptionService service, string probe)
    {
        try
        {
            _ = service.Decrypt(probe);
            return new StartupDiagnosticsEntry("credential-encryption-existing-data",
                StartupDiagnosticsStatus.Ok, "existing-ciphertext probe decrypted successfully");
        }
        catch (PubSoftDecryptionException ex)
        {
            return new StartupDiagnosticsEntry("credential-encryption-existing-data",
                StartupDiagnosticsStatus.Error,
                $"configured key ring cannot decrypt existing ciphertext ({ex.Reason}, keyId={ex.KeyId ?? "?"}) - verify the deployed key material against the data source");
        }
    }
}

/// <summary>Options for <see cref="EncryptionStartupDiagnostics"/>.</summary>
public sealed class EncryptionDiagnosticsOptions
{
    /// <summary>Optional ciphertext produced under the production key - proves the configured
    /// ring can still decrypt existing data (catches key drift that a roundtrip cannot).</summary>
    public string? ProbeCiphertext { get; set; }
}
