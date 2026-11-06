using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PubSoft.Notifications;

/// <summary>Microsoft Teams incoming-webhook channel.</summary>
public sealed class TeamsWebhookChannel : INotificationChannel
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TeamsWebhookOptions _options;
    private readonly ILogger<TeamsWebhookChannel>? _logger;

    /// <summary>Creates the channel.</summary>
    public TeamsWebhookChannel(
        IHttpClientFactory httpClientFactory,
        IOptions<TeamsWebhookOptions> options,
        ILogger<TeamsWebhookChannel>? logger = null)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "teams";

    /// <inheritdoc />
    public async Task<NotificationResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.WebhookUrl))
            return NotificationResult.Fail("Teams webhook URL not configured");

        var themeColor = message.Severity switch
        {
            NotificationSeverity.Critical => "FF0000",
            NotificationSeverity.Warning => "FFA500",
            _ => "0078D7",
        };

        var card = new JsonObject
        {
            ["@type"] = "MessageCard",
            ["@context"] = "https://schema.org/extensions",
            ["themeColor"] = themeColor,
            ["title"] = message.Title ?? "Notification",
            ["text"] = message.Content,
        };

        if (!string.IsNullOrEmpty(message.Source))
            card["summary"] = $"[{message.Source}] {message.Title ?? message.Content}";

        try
        {
            var http = _httpClientFactory.CreateClient(NotificationChannelExtensions.TeamsHttpClientName);
            var content = new StringContent(card.ToJsonString(), Encoding.UTF8, "application/json");
            var resp = await http.PostAsync(_options.WebhookUrl, content, cancellationToken).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (resp.IsSuccessStatusCode)
            {
                _logger?.LogDebug("Teams sent: {Title}", message.Title);
                return NotificationResult.Ok();
            }

            _logger?.LogError("Teams failed: {Status} {Body}", resp.StatusCode, body);
            return NotificationResult.Fail($"HTTP {(int)resp.StatusCode}: {body}");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Teams exception");
            return NotificationResult.Fail(ex.Message);
        }
    }
}

/// <summary>Teams incoming-webhook channel options.</summary>
public sealed class TeamsWebhookOptions
{
    /// <summary>Incoming-webhook URL.</summary>
    public string WebhookUrl { get; set; } = "";

    /// <summary>
    /// Permits a plaintext <c>http</c> webhook URL. Non-loopback <c>http</c> is rejected
    /// unless this is set. Loopback endpoints are exempt for local development.
    /// </summary>
    public bool AllowInsecureUrl { get; set; }
}
