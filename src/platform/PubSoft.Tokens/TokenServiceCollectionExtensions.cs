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