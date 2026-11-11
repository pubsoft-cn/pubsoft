using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.AspNetCore;
using PubSoft.Cryptography;
using PubSoft.Monitoring;
using Xunit;

namespace PubSoft.Host.Tests;

/// <summary>R7: probe failures must yield Error entries, never throw.</summary>
public sealed class EncryptionStartupDiagnosticsWaveTests
{
    private sealed class Ring(string id, byte[] key) : IEncryptionKeyRing
    {
        private readonly byte[] _key = key;
        public string ActiveKeyId { get; } = id;
        public bool TryGetKey(string keyId, out ReadOnlyMemory<byte> key)
        { key = _key; return keyId == id; }
    }

    private static ServiceProvider Build(bool withService)
    {
        var services = new ServiceCollection();
        if (withService)
            services.AddSingleton(new CredentialEncryptionService(
                new AesGcmEncryptor(new Ring("k1", RandomNumberGenerator.GetBytes(32))), "legacy"));
        return services.BuildServiceProvider();
    }

    [Fact]
    public void NoServiceWarnsNotRegistered()
    {
        var entries = new EncryptionStartupDiagnostics(Build(false)).Collect().ToList();
        Assert.Contains(entries, e => e.Area == "credential-encryption" && e.Status == StartupDiagnosticsStatus.Warning);
    }

    [Fact]
    public void ValidServiceRoundtripsOk()
    {
        var entries = new EncryptionStartupDiagnostics(Build(true)).Collect().ToList();
        Assert.Contains(entries, e => e.Area == "credential-encryption-roundtrip" && e.Status == StartupDiagnosticsStatus.Ok);
    }

    [Fact]
    public void MalformedProbeYieldsErrorEntryNotException()
    {
        var options = new EncryptionDiagnosticsOptions { ProbeCiphertext = "%%%not-valid-base64%%%" };
        var entries = new EncryptionStartupDiagnostics(Build(true), options).Collect().ToList();
        Assert.Contains(entries, e => e.Area == "credential-encryption-existing-data"
            && e.Status == StartupDiagnosticsStatus.Error);
    }
}
