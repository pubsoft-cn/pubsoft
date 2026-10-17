using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PubSoft.Notifications;

/// <summary>
/// Aliyun SMS channel (RPC-style signed request, HMAC-SHA1 signature).
/// The first <see cref="NotificationMessage.Recipients"/> entry is used as the phone number;
/// <see cref="NotificationMessage.TemplateCode"/> and <see cref="NotificationMessage.TemplateParameters"/>
/// map to the provider template fields.
/// </summary>
public sealed class AliyunSmsChannel : INotificationChannel
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AliyunSmsOptions _options;
    private readonly ILogger<AliyunSmsChannel>? _logger;

    public string Name => "sms";

    public AliyunSmsChannel(
        IHttpClientFactory httpClientFactory,
        IOptions<AliyunSmsOptions> options,
        ILogger<AliyunSmsChannel>? logger = null)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<NotificationResult> SendAsync(NotificationMessage message, CancellationToken cancellationToken = default)
    {
        var phone = message.Recipients.FirstOrDefault();
        if (string.IsNullOrEmpty(phone))
            return NotificationResult.Fail("No recipient phone");

        if (string.IsNullOrEmpty(_options.AccessKeyId) || string.IsNullOrEmpty(_options.AccessKeySecret))
            return NotificationResult.Fail("Aliyun SMS credentials not configured");

        if (string.IsNullOrEmpty(_options.SignName))
            return NotificationResult.Fail("Aliyun SMS SignName not configured");

        var templateParamJson = new JsonObject();
        foreach (var pair in message.TemplateParameters)
            templateParamJson[pair.Key] = pair.Value;

        var parameters = BuildCommonParams(phone, message.TemplateCode ?? "", templateParamJson.ToJsonString());
        SignParameters(parameters);

        try
        {
            var http = _httpClientFactory.CreateClient(NotificationChannelExtensions.AliyunSmsHttpClientName);
            var content = new FormUrlEncodedContent(parameters);
            var resp = await http.PostAsync($"https://{_options.Endpoint}", content, cancellationToken).ConfigureAwait(false);
            var body = await resp.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var node = JsonNode.Parse(body);
            var code = node?["Code"]?.GetValue<string>();

            if (code == "OK")
            {
                var bizId = node?["BizId"]?.GetValue<string>();
                _logger?.LogDebug("SMS sent: {Phone} {BizId}", phone, bizId);
                return NotificationResult.Ok(bizId);
            }

            var errMsg = node?["Message"]?.GetValue<string>() ?? body;
            _logger?.LogError("SMS failed: {Phone} {Code} {Message}", phone, code, errMsg);
            return NotificationResult.Fail(errMsg);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "SMS exception: {Phone}", phone);
            return NotificationResult.Fail(ex.Message);
        }
    }

    private Dictionary<string, string> BuildCommonParams(string phone, string templateCode, string templateParam)
    {
        return new Dictionary<string, string>
        {
            ["SignatureMethod"] = "HMAC-SHA1",
            ["SignatureNonce"] = Guid.NewGuid().ToString(),
            ["SignatureVersion"] = "1.0",
            ["AccessKeyId"] = _options.AccessKeyId,
            ["Timestamp"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["Format"] = "JSON",
            ["Version"] = "2017-05-25",
            ["Action"] = "SendSms",
            ["RegionId"] = _options.RegionId,
            ["PhoneNumbers"] = phone,
            ["SignName"] = _options.SignName,
            ["TemplateCode"] = templateCode,
            ["TemplateParam"] = templateParam,
        };
    }

    private void SignParameters(Dictionary<string, string> parameters)
    {
        // Aliyun RPC signature: sort → RFC3986 encode → HMAC-SHA1(secret&) → Base64
        var sorted = parameters
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{UrlEncode(p.Key)}={UrlEncode(p.Value)}");
        var canonicalized = string.Join("&", sorted);
        var stringToSign = "GET&" + UrlEncode("/") + "&" + UrlEncode(canonicalized);

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(_options.AccessKeySecret + "&"));
        parameters["Signature"] = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
    }

    private static string UrlEncode(string value)
    {
        var sb = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            if ((b >= 'A' && b <= 'Z') || (b >= 'a' && b <= 'z') ||
                (b >= '0' && b <= '9') || b is (byte)'-' or (byte)'_' or (byte)'.' or (byte)'~')
                sb.Append((char)b);
            else
                sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }
}

/// <summary>Aliyun SMS channel options.</summary>
public sealed class AliyunSmsOptions
{
    public string AccessKeyId { get; set; } = "";
    public string AccessKeySecret { get; set; } = "";
    public string SignName { get; set; } = "";
    public string Endpoint { get; set; } = "dysmsapi.aliyuncs.com";
    public string RegionId { get; set; } = "cn-hangzhou";
}
