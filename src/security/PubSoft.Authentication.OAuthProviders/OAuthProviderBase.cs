using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PubSoft.OAuth;

/// <summary>
/// Base class for OAuth providers: channel credential resolution plus reflection-free JSON helpers.
/// Derived classes implement the provider-specific API calls.
/// </summary>
public abstract class OAuthProviderBase : IOAuthProvider
{
    protected readonly IOAuthChannelConfigProvider ConfigProvider;
    protected readonly HttpClient HttpClient;
    protected readonly ILogger? Logger;

    protected OAuthProviderBase(
        IOAuthChannelConfigProvider configProvider,
        HttpClient httpClient,
        ILogger? logger = null)
    {
        ConfigProvider = configProvider;
        HttpClient = httpClient;
        Logger = logger;
    }

    public abstract string Provider { get; }

    public virtual bool SupportsChannelType(string channelType) => true;

    /// <summary>Resolves channel credentials for the given AppId; fails closed when missing.</summary>
    protected virtual async Task<OAuthChannelConfig> ResolveConfigAsync(string appId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new InvalidOperationException($"{Provider} login failed: AppId not provided");

        var config = await ConfigProvider.GetByAppIdAsync(appId, ct)
            ?? throw new InvalidOperationException($"{Provider} login failed: no channel config found for AppId={appId}");

        if (string.IsNullOrWhiteSpace(config.AppSecret))
            throw new InvalidOperationException($"{Provider} login failed: channel {appId} has no AppSecret configured");

        return config;
    }

    public abstract Task<OAuthAuthorizeUrlResult> GetAuthorizeUrlAsync(OAuthAuthorizeUrlRequest request, CancellationToken ct = default);
    public abstract Task<OAuthCodeExchangeResult> ExchangeCodeAsync(string appId, string code, string? channelType = null, CancellationToken ct = default);
    public abstract Task<OAuthUserInfoResult> GetUserInfoAsync(string appId, string accessToken, CancellationToken ct = default);

    /// <summary>Phone numbers are opt-in; providers that support it override this.</summary>
    public virtual Task<OAuthPhoneResult> GetPhoneNumberAsync(string appId, string code, CancellationToken ct = default)
        => Task.FromResult(OAuthPhoneResult.Fail("This provider does not support phone-number retrieval"));

    /// <summary>GET a URL and parse the JSON body without any serializer metadata.</summary>
    protected async Task<JsonElement> FetchJsonAsync(string url, CancellationToken ct)
    {
        var text = await HttpClient.GetStringAsync(url, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    /// <summary>POST a body and parse the JSON response without any serializer metadata.</summary>
    protected async Task<JsonElement> PostJsonAsync(string url, HttpContent body, CancellationToken ct)
    {
        using var response = await HttpClient.PostAsync(url, body, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    /// <summary>Send a request and parse the JSON response without any serializer metadata.</summary>
    protected async Task<JsonElement> SendJsonAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await HttpClient.SendAsync(request, ct).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    /// <summary>Extracts a string field; numeric and boolean JSON values are returned as their raw text
    /// (several providers emit ids as numbers rather than strings).</summary>
    protected static string? GetString(JsonElement elem, string name) =>
        !elem.TryGetProperty(name, out var p) ? null : p.ValueKind switch
        {
            JsonValueKind.String => p.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => p.GetRawText(),
            _ => null,
        };

    protected static int GetInt(JsonElement elem, string name, int defaultValue = 0) =>
        elem.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : defaultValue;
}
