using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace PubSoft.Tokens;

public static class TokenServiceCollectionExtensions
{
    public static IServiceCollection AddPubSoftTokens(
        this IServiceCollection services,
        Action<TokenManagerOptions>? configure = null,
        Action<TokenHandlerOptions>? configureHandler = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<TokenManagerOptions>()
            .Configure(options => configure?.Invoke(options))
            .Validate(options => options.IsValid(out _), "Token manager options are invalid.")
            .ValidateOnStart();
        services.AddOptions<TokenHandlerOptions>()
            .Configure(options => configureHandler?.Invoke(options));
        services.AddHttpClient(HttpTokenProvider.HttpClientName);
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<ITokenProvider, HttpTokenProvider>();
        return services;
    }

    /// <summary>Registers <see cref="MachineTokenProvider"/> as the <see cref="ITokenProvider"/> for HS256 machine credentials.</summary>
    /// <remarks>Issuer/audience/signing key come from the configured options — never hardcode them; signers and validators must agree exactly (comparison is ordinal, case-sensitive).</remarks>
    public static IServiceCollection AddPubSoftMachineTokens(
        this IServiceCollection services,
        Action<MachineTokenOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<MachineTokenOptions>()
            .Configure(configure)
            .Validate(options => options.IsValid(out _), "Machine token options are invalid.")
            .ValidateOnStart();
        services.TryAddSingleton<MachineTokenProvider>();
        services.TryAddSingleton<ITokenProvider>(provider => provider.GetRequiredService<MachineTokenProvider>());
        // Keyed registration removes ordering ambiguity: when a host registers both the HTTP
        // provider and machine tokens, the keyed "machine" name resolves the local issuer
        // deterministically — [FromKeyedServices(MachineTokenProvider.ServiceKey)].
        services.AddKeyedSingleton<ITokenProvider>(
            MachineTokenProvider.ServiceKey,
            (provider, _) => provider.GetRequiredService<MachineTokenProvider>());
        return services;
    }

    /// <summary>Binds <see cref="MachineTokenOptions"/> from a configuration section (default <c>Jwt</c>: <c>Issuer</c>/<c>Audience</c>/<c>SecretKey</c>; <c>SigningKey</c> is accepted as an alias).</summary>
    public static IServiceCollection AddPubSoftMachineTokens(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "Jwt")
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionName);
        var section = configuration.GetSection(sectionName);
        return services.AddPubSoftMachineTokens(options => BindMachineTokenOptions(section, options));
    }

    // Explicit reads keep this AOT-safe; the reflection-based configuration binder is not referenced.
    private static void BindMachineTokenOptions(IConfiguration section, MachineTokenOptions options)
    {
        options.Issuer = section["Issuer"] ?? options.Issuer;
        options.Audience = section["Audience"] ?? options.Audience;
        // "SecretKey" is the established key name on the validation side (Jwt:SecretKey);
        // "SigningKey" remains accepted as the options-property alias.
        options.SigningKey = section["SecretKey"] ?? section["SigningKey"] ?? options.SigningKey;
        if (section["KeyId"] is { } keyId)
            options.KeyId = keyId;
        if (TimeSpan.TryParse(section["Lifetime"], CultureInfo.InvariantCulture, out var lifetime))
            options.Lifetime = lifetime;
        if (TimeSpan.TryParse(section["RefreshBefore"], CultureInfo.InvariantCulture, out var refreshBefore))
            options.RefreshBefore = refreshBefore;

        foreach (var credentialSection in section.GetSection("Credentials").GetChildren())
        {
            var credential = new MachineTokenCredential
            {
                KeyId = credentialSection["KeyId"],
                SigningKey = credentialSection["SecretKey"] ?? credentialSection["SigningKey"]
            };
            foreach (var claim in credentialSection.GetSection("Claims").GetChildren())
                credential.Claims[claim.Key] = claim.Value ?? string.Empty;
            options.Credentials[credentialSection.Key] = credential;
        }
    }

    public static IHttpClientBuilder AddTokenHandler(
        this IHttpClientBuilder builder,
        string credentialKey,
        string? headerName = null,
        string? headerPrefix = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(credentialKey);
        return builder.AddHttpMessageHandler(services => new TokenDelegatingHandler(
            services.GetRequiredService<ITokenProvider>(),
            services.GetRequiredService<Microsoft.Extensions.Options.IOptions<TokenHandlerOptions>>(),
            credentialKey,
            headerName,
            headerPrefix));
    }
}
