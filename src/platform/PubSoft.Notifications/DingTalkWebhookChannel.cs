using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PubSoft.Notifications;

/// <summary>DingTalk robot webhook channel. When <see cref="DingTalkWebhookOptions.Secret"/> is set the request is signed per the DingTalk robot spec.</summary>
/// <summary>DingTalk group-bot webhook channel.</summary>
public sealed class DingTalkWebhookChannel : INotificationChannel
{
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DingTalkWebhookOptions _options;
    private readonly ILogger<DingTalkWebhookChannel>? _logger;

    /// <summary>Creates the channel.</summary>
    public DingTalkWebhookChannel(
        IHttpClientFactory httpClientFactory,
        IOptions<DingTalkWebhookOptions> options,
        ILogger<DingTalkWebhookChannel>? logger = null)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "dingtalk";

    /// <inheritdoc />
    public async Task<NotificationResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.WebhookUrl))
            return NotificationResult.Fail("DingTalk webhook URL not configured");

        var text = message.Title is not null
            ? $"**{message.Title}**\n\n{message.Content}"
            : message.Content;

        var payload = new JsonObject
        {
            ["msgtype"] = "text",
            ["text"] = new JsonObject { ["content"] = text },
        };

        var url = _options.WebhookUrl;
        if (!string.IsNullOrEmpty(_options.Secret))
        {
            var timestamp = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
            var sign = ComputeSign(timestamp, _options.Secret);
            url += $"&timestamp={timestamp}&sign={Uri.EscapeDataString(sign)}";
        }

        try
        {
            var http = _httpClientFactory.CreateClient(NotificationChannelExtensions.DingTalkHttpClientName);
            var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            var resp = await http.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var node = JsonNode.Parse(body);
            var errcode = node?["errcode"]?.GetValue<int>();

            if (errcode == 0)
            {
                _logger?.LogDebug("DingTalk sent: {Title}", message.Title);
                return NotificationResult.Ok();
            }

            var errmsg = node?["errmsg"]?.GetValue<string>() ?? body;
            _logger?.LogError("DingTalk failed: {ErrCode} {ErrMsg}", errcode, errmsg);
            return NotificationResult.Fail(errmsg);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "DingTalk exception");
            return NotificationResult.Fail(ex.Message);
        }
    }

    private static string ComputeSign(long timestamp, string secret)
    {
        var stringToSign = $"{timestamp}\n{secret}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
    }
}

/// <summary>DingTalk robot webhook channel options.</summary>
/// <summary>DingTalk webhook settings.</summary>
public sealed class DingTalkWebhookOptions
{
    /// <summary>Webhook URL including the access token.</summary>
    public string WebhookUrl { get; set; } = "";
    /// <summary>Optional HMAC secret for signed webhooks.</summary>
    public string Secret { get; set; } = "";

    /// <summary>
    /// Permits a plaintext <c>http</c> webhook URL. Signed DingTalk requests embed the
    /// signature in the query -- non-loopback <c>http</c> is rejected unless this is set.
    /// Loopback endpoints are exempt for local development.
    /// </summary>
    public bool AllowInsecureUrl { get; set; }
}
