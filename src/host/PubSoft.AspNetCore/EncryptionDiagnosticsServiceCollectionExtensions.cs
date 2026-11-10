using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Monitoring;

namespace PubSoft.AspNetCore;

/// <summary>Registration for the encryption startup probe.</summary>
public static class EncryptionDiagnosticsServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="EncryptionStartupDiagnostics"/> from configuration:
    /// <c>Encryption:ProbeCiphertext</c> (a ciphertext produced under the production key) makes the
    /// startup report verify the configured ring can decrypt existing data - the "key configured
    /// but cannot decrypt" state surfaces at startup instead of first request. Without it, only
    /// the roundtrip self-consistency probe runs.
    /// </summary>
    public static IServiceCollection AddPubSoftEncryptionDiagnostics(
        this IServiceCollection services, IConfiguration configuration, string sectionName = "Encryption")
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        var probe = configuration.GetSection(sectionName)["ProbeCiphertext"];
        services.AddSingleton<IStartupDiagnostics>(sp => new EncryptionStartupDiagnostics(
            sp, new EncryptionDiagnosticsOptions { ProbeCiphertext = probe }));
        return services;
    }
}
