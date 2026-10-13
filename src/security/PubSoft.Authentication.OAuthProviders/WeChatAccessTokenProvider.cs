using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace PubSoft.OAuth;

/// <summary>Result of a WeChat global access_token fetch (cgi-bin/token).</summary>
public sealed class WeChatAccessTokenResult
{
    public bool Success { get; set; }
    public string AccessToken { get; set; } = "";
    public string? Message { get; set; }

    public static WeChatAccessTokenResult Ok(string token) => new() { Success = true, AccessToken = token };
    public static WeChatAccessTokenResult Fail(string msg) => new() { Success = false, Message = msg };
}

/// <summary>
/// WeChat global access_token provider (cached). Shared between <see cref="WeChatOAuthProvider"/>
/// and host code so a single cache prevents cgi-bin/token rate limits.
/// </summary>
public interface IWeChatAccessTokenProvider
{
    /// <summary>Gets the global access_token (2h lifetime, refreshed 5 min early). Cache is keyed per AppId.</summary>
    Task<WeChatAccessTokenResult> GetAsync(string appId, CancellationToken ct = default);
}

/// <summary>
/// Default implementation — process-wide per-AppId cache (typed clients are transient, so the
/// cache intentionally lives in static state shared across resolutions).
/// </summary>
public sealed class WeChatAccessTokenProvider : IWeChatAccessTokenProvider
{
    private readonly IOAuthChannelConfigProvider _configProvider;
    private readonly HttpClient _httpClient;
    private readonly ILogger? _logger;

    private static readonly ConcurrentDictionary<string, (string token, DateTime expiresAt)> _cache = new(StringComparer.Ordinal);

    public WeChatAccessTokenProvider(
        IOAuthChannelConfigProvider configProvider,
        HttpClient httpClient,
        ILogger<WeChatAccessTokenProvider>? logger = null)
    {
        _configProvider = configProvider;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<WeChatAccessTokenResult> GetAsync(string appId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(appId))
            return WeChatAccessTokenResult.Fail("AppId not provided");

        if (_cache.TryGetValue(appId, out var cached) && cached.expiresAt > DateTime.UtcNow.AddMinutes(5))
            return WeChatAccessTokenResult.Ok(cached.token);

        var config = await _configProvider.GetByAppIdAsync(appId, ct).ConfigureAwait(false);
        if (config == null)
            return WeChatAccessTokenResult.Fail($"No channel config found for AppId={appId}");
        if (string.IsNullOrWhiteSpace(config.AppSecret))
            return WeChatAccessTokenResult.Fail($"Channel {appId} has no AppSecret configured");

        var url = $"https://api.weixin.qq.com/cgi-bin/token?grant_type=client_credential&appid={config.AppId}&secret={config.AppSecret}";
        var sw = Stopwatch.StartNew();
        var text = await _httpClient.GetStringAsync(url, ct).ConfigureAwait(false);
        sw.Stop();
        _logger?.LogInformation("WeChat access_token appId={AppId} elapsed={Elapsed}ms", appId, sw.ElapsedMilliseconds);

        using var doc = System.Text.Json.JsonDocument.Parse(text);
        var response = doc.RootElement;
        var errCode = response.TryGetProperty("errcode", out var e) && e.ValueKind == System.Text.Json.JsonValueKind.Number
            ? e.GetInt32() : 0;
        if (errCode != 0)
            return WeChatAccessTokenResult.Fail(
                response.TryGetProperty("errmsg", out var m) ? m.GetString() ?? "Failed to get access_token" : "Failed to get access_token");

        var token = response.TryGetProperty("access_token", out var t) ? t.GetString() ?? "" : "";
        var expiresIn = response.TryGetProperty("expires_in", out var x) && x.ValueKind == System.Text.Json.JsonValueKind.Number
            ? x.GetInt32() : 7200;

        _cache[appId] = (token, DateTime.UtcNow.AddSeconds(expiresIn));
        return WeChatAccessTokenResult.Ok(token);
    }
}
