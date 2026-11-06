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

        var config = await ConfigProvider.GetByAppIdAsync(appId, ct).ConfigureAwait(false)
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

    /// <summary>上游响应体大小上限 — 异常/恶意提供方不得向内存灌入无限数据（1 MiB 足够任何 OAuth JSON 载荷）。</summary>
    private const int MaxResponseBytes = 1_048_576;

    /// <summary>GET a URL and parse the JSON body without any serializer metadata.</summary>
    protected async Task<JsonElement> FetchJsonAsync(string url, CancellationToken ct)
    {
        var text = await ReadBoundedStringAsync(await HttpClient.GetAsync(url, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    /// <summary>POST a body and parse the JSON response without any serializer metadata.</summary>
    protected async Task<JsonElement> PostJsonAsync(string url, HttpContent body, CancellationToken ct)
    {
        using var response = await HttpClient.PostAsync(url, body, ct).ConfigureAwait(false);
        var text = await ReadBoundedStringAsync(response, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    /// <summary>Send a request and parse the JSON response without any serializer metadata.</summary>
    protected async Task<JsonElement> SendJsonAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await HttpClient.SendAsync(request, ct).ConfigureAwait(false);
        var text = await ReadBoundedStringAsync(response, ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    /// <summary>响应体限长读取——超限抛 InvalidDataException，拒绝把整条上游负载读进内存。</summary>
    private static async Task<string> ReadBoundedStringAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using (response)
        {
            if (response.Content.Headers.ContentLength > MaxResponseBytes)
                throw new InvalidDataException($"OAuth provider response exceeded {MaxResponseBytes} bytes");
            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var bytes = new byte[MaxResponseBytes + 1];
            var total = 0;
            int read;
            while (total <= MaxResponseBytes
                   && (read = await stream.ReadAsync(bytes.AsMemory(total, Math.Min(8192, bytes.Length - total)), ct).ConfigureAwait(false)) > 0)
                total += read;
            if (total > MaxResponseBytes)
                throw new InvalidDataException($"OAuth provider response exceeded {MaxResponseBytes} bytes");
            return System.Text.Encoding.UTF8.GetString(bytes, 0, total);
        }
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
