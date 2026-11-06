using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace PubSoft.OAuth;

/// <summary>
/// Alipay OAuth provider -- direct gateway calls (alipay.system.oauth.token + alipay.user.info.share)
/// with inline RSA2 signing and response verification.
/// Credentials: AppId = Alipay application id. AppSecret is unused (RSA2 signs instead).
/// ExtraJson keys: PrivateKeyPem (app private key PEM), PlatformPublicKeyPem (Alipay platform public key PEM),
/// GatewayUrl (default https://openapi.alipay.com/gateway.do).
/// </summary>
public sealed partial class AlipayOAuthProvider : OAuthProviderBase
{
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    /// <summary>Creates the provider.</summary>
    public AlipayOAuthProvider(
        IOAuthChannelConfigProvider configProvider,
        HttpClient httpClient,
        ILogger<AlipayOAuthProvider>? logger = null)
        : base(configProvider, httpClient, logger) { }

    public override string Provider => OAuthProviderNames.Alipay;

    public override bool SupportsChannelType(string channelType) =>
        channelType is OAuthChannelTypes.MiniProgram or OAuthChannelTypes.Web;

    public override Task<OAuthAuthorizeUrlResult> GetAuthorizeUrlAsync(OAuthAuthorizeUrlRequest request, CancellationToken ct = default)
    {
        var state = request.State ?? Guid.NewGuid().ToString("N");
        var redirectUri = Uri.EscapeDataString(request.RedirectUri ?? "");
        var url = $"https://openauth.alipay.com/oauth2/publicAppAuthorize.htm?app_id={Uri.EscapeDataString(request.AppId)}&scope=auth_user&redirect_uri={redirectUri}&state={Uri.EscapeDataString(state)}";

        return Task.FromResult(new OAuthAuthorizeUrlResult
        {
            Success = true,
            AuthorizeUrl = url,
            State = state,
            AppId = request.AppId,
        });
    }

    public override async Task<OAuthCodeExchangeResult> ExchangeCodeAsync(string appId, string code, string? channelType = null, CancellationToken ct = default)
    {
        try
        {
            var biz = new Dictionary<string, string>(System.StringComparer.Ordinal) { ["grant_type"] = "authorization_code", ["code"] = code };
            var resp = await ExecuteAlipayGatewayAsync(appId, "alipay.system.oauth.token", biz, ct: ct).ConfigureAwait(false);
            var openId = GetString(resp, "open_id") ?? "";
            var userId = GetString(resp, "user_id") ?? "";
            var resolved = !string.IsNullOrWhiteSpace(openId) ? openId : userId;
            if (string.IsNullOrWhiteSpace(resolved))
                return OAuthCodeExchangeResult.Fail("Alipay login failed: no user identifier returned");

            return new OAuthCodeExchangeResult
            {
                Success = true,
                OpenId = openId,
                UserId = userId,
                AccessToken = GetString(resp, "access_token"),
                RefreshToken = GetString(resp, "refresh_token"),
            };
        }
        catch (AlipayOAuthException ex)
        {
            Logger?.LogWarning("Alipay OAuth failed: {Code}: {Msg}", ex.Code, ex.Message);
            return OAuthCodeExchangeResult.Fail(ex.Message);
        }
    }

    public override async Task<OAuthUserInfoResult> GetUserInfoAsync(string appId, string accessToken, CancellationToken ct = default)
    {
        try
        {
            var resp = await ExecuteAlipayGatewayAsync(appId, "alipay.user.info.share", new Dictionary<string, string>(System.StringComparer.Ordinal), authToken: accessToken, ct: ct).ConfigureAwait(false);
            return new OAuthUserInfoResult
            {
                Success = true,
                UserId = GetString(resp, "user_id"),
                OpenId = GetString(resp, "open_id") ?? "",
                NickName = GetString(resp, "nick_name"),
                AvatarUrl = GetString(resp, "avatar"),
                Gender = GetString(resp, "gender"),
                Province = GetString(resp, "province"),
                City = GetString(resp, "city"),
            };
        }
        catch (AlipayOAuthException ex)
        {
            return OAuthUserInfoResult.Fail(ex.Message);
        }
    }

    public override async Task<OAuthPhoneResult> GetPhoneNumberAsync(string appId, string code, CancellationToken ct = default)
    {
        try
        {
            var tokenBiz = new Dictionary<string, string>(System.StringComparer.Ordinal) { ["grant_type"] = "authorization_code", ["code"] = code };
            var tokenResp = await ExecuteAlipayGatewayAsync(appId, "alipay.system.oauth.token", tokenBiz, ct: ct).ConfigureAwait(false);
            var accessToken = GetString(tokenResp, "access_token");
            if (string.IsNullOrWhiteSpace(accessToken))
                return OAuthPhoneResult.Fail("Alipay phone query failed: no access_token returned");

            var resp = await ExecuteAlipayGatewayAsync(appId, "alipay.user.info.share", new Dictionary<string, string>(System.StringComparer.Ordinal), authToken: accessToken, ct: ct).ConfigureAwait(false);
            var phone = GetString(resp, "mobile");
            if (string.IsNullOrWhiteSpace(phone))
                return OAuthPhoneResult.Fail("Alipay user has no bound phone number");

            return new OAuthPhoneResult
            {
                Success = true,
                PhoneNumber = phone,
                PurePhoneNumber = phone,
                CountryCode = "86",
            };
        }
        catch (AlipayOAuthException ex)
        {
            return OAuthPhoneResult.Fail(ex.Message);
        }
    }

    /// <summary>Alipay channels sign with RSA2 from ExtraJson and intentionally carry no AppSecret.</summary>
    protected override async Task<OAuthChannelConfig> ResolveConfigAsync(string appId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new InvalidOperationException($"{Provider} login failed: AppId not provided");

        return await ConfigProvider.GetByAppIdAsync(appId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"{Provider} login failed: no channel config found for AppId={appId}");
    }

    // ── RSA2 sign/verify helpers ──

    private static string SerializeBizContent(Dictionary<string, string> bizContent)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var (key, value) in bizContent)
                writer.WriteString(key, value);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string BuildSignContent(Dictionary<string, string> paramDict)
    {
        var sorted = paramDict
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key}={kv.Value}");
        return string.Join("&", sorted);
    }

    private static string SignWithRsa2(string content, string privateKey)
    {
        var keyBytes = ConvertPkcs1OrPkcs8ToPrivateKey(privateKey);
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(keyBytes, out _);
        var dataBytes = Encoding.UTF8.GetBytes(content);
        var sigBytes = rsa.SignData(dataBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return Convert.ToBase64String(sigBytes);
    }

    private static bool VerifyWithRsa2(string content, string sign, string alipayPublicKey, Encoding encoding)
    {
        try
        {
            var keyBytes = ConvertPublicKeyToRsa(alipayPublicKey);
            using var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(keyBytes, out _);
            var dataBytes = encoding.GetBytes(content);
            var sigBytes = Convert.FromBase64String(sign);
            return rsa.VerifyData(dataBytes, sigBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }

    // Strips any PEM armor (BEGIN/END ... KEY) without embedding marker literals.
    [System.Text.RegularExpressions.GeneratedRegex("-----[^\r\n-]+-----", System.Text.RegularExpressions.RegexOptions.CultureInvariant)]
    private static partial System.Text.RegularExpressions.Regex PemArmorRegex();

    private static byte[] ConvertPkcs1OrPkcs8ToPrivateKey(string privateKey)
    {
        var cleaned = PemArmorRegex().Replace(privateKey, string.Empty) // pubsoft:exempt PUBSOFT022 "char overload cannot delete characters"
            .Replace("\r", "").Replace("\n", "").Replace(" ", "").Trim();
        var raw = Convert.FromBase64String(cleaned);
        using var rsa = RSA.Create();
        try
        {
            rsa.ImportPkcs8PrivateKey(raw, out _);
            return raw;
        }
        catch
        {
            rsa.ImportRSAPrivateKey(raw, out _);
            return rsa.ExportPkcs8PrivateKey();
        }
    }

    private static byte[] ConvertPublicKeyToRsa(string publicKey)
    {
        var cleaned = publicKey // pubsoft:exempt PUBSOFT022 "char overload cannot delete characters"
            .Replace("-----BEGIN PUBLIC KEY-----", "")
            .Replace("-----END PUBLIC KEY-----", "")
            .Replace("\r", "").Replace("\n", "").Replace(" ", "").Trim();
        return Convert.FromBase64String(cleaned);
    }

    private static string? ExtractResponseRaw(string respText, string responseKey)
    {
        var key = "\"" + responseKey + "\":";
        var idx = respText.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0) return null;
        idx += key.Length;
        while (idx < respText.Length && (respText[idx] == ' ' || respText[idx] == '\n' || respText[idx] == '\r' || respText[idx] == '\t'))
            idx++;
        if (idx >= respText.Length || respText[idx] != '{') return null;
        var depth = 0;
        var inString = false;
        var escape = false;
        var start = idx;
        for (; idx < respText.Length; idx++)
        {
            var c = respText[idx];
            if (escape) { escape = false; continue; }
            if (c == '\\') { escape = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                    return respText.Substring(start, idx - start + 1);
            }
        }
        return null;
    }

    // ── ExtraJson parsing (reflection-free) ──

    private static (string? privateKeyPem, string? platformPublicKeyPem, string? gatewayUrl) ParseAlipayExtra(string? extraJson)
    {
        if (string.IsNullOrWhiteSpace(extraJson))
            return (null, null, null);
        try
        {
            using var doc = JsonDocument.Parse(extraJson);
            var root = doc.RootElement;
            return (GetString(root, "PrivateKeyPem") ?? GetString(root, "privateKeyPem"),
                GetString(root, "PlatformPublicKeyPem") ?? GetString(root, "platformPublicKeyPem"),
                GetString(root, "GatewayUrl") ?? GetString(root, "gatewayUrl"));
        }
        catch
        {
            return (null, null, null);
        }
    }

    // ── Alipay gateway call (inline RSA2 sign + verify) ──

    private async Task<JsonElement> ExecuteAlipayGatewayAsync(
        string appId, string method, Dictionary<string, string> bizContent,
        string? authToken = null, CancellationToken ct = default)
    {
        var config = await ResolveConfigAsync(appId, ct).ConfigureAwait(false);
        var (privateKeyPem, platformPublicKeyPem, gatewayUrl) = ParseAlipayExtra(config.ExtraJson);
        var privateKey = privateKeyPem ?? throw new AlipayOAuthException("NO_PRIVATE_KEY", "Alipay channel has no app private key (ExtraJson.PrivateKeyPem)");
        gatewayUrl ??= "https://openapi.alipay.com/gateway.do";

        var paramDict = new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            ["app_id"] = config.AppId,
            ["method"] = method,
            ["format"] = "JSON",
            ["charset"] = "utf-8",
            ["sign_type"] = "RSA2",
            ["timestamp"] = _timeProvider.GetLocalNow().DateTime.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
            ["version"] = "1.0",
        };
        if (!string.IsNullOrEmpty(authToken))
            paramDict["auth_token"] = authToken;

        paramDict["biz_content"] = SerializeBizContent(bizContent);

        var signContent = BuildSignContent(paramDict);
        paramDict["sign"] = SignWithRsa2(signContent, privateKey);

        var formData = new FormUrlEncodedContent(paramDict);
        var sw = Stopwatch.StartNew();
        var resp = await HttpClient.PostAsync(gatewayUrl, formData, ct).ConfigureAwait(false);
        if (resp.Content.Headers.ContentLength > 1_048_576)
            throw new InvalidDataException("Alipay response exceeded 1 MiB");
        var respBytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        if (respBytes.Length > 1_048_576)
            throw new InvalidDataException("Alipay response exceeded 1 MiB");
        sw.Stop();
        Logger?.LogInformation("Alipay {Method} appId={AppId} elapsed={Elapsed}ms", method, appId, sw.ElapsedMilliseconds);

        // Alipay may respond in GBK; GB18030 requires the code-pages provider, fall back to UTF-8.
        var charset = "UTF-8";
        var contentType = resp.Content.Headers.ContentType?.CharSet;
        if (!string.IsNullOrEmpty(contentType) &&
            (contentType.Contains("GBK", StringComparison.OrdinalIgnoreCase) ||
             contentType.Contains("GB2312", StringComparison.OrdinalIgnoreCase)))
            charset = "GB18030";
        Encoding encoding;
        try { encoding = Encoding.GetEncoding(charset); }
        catch { encoding = Encoding.UTF8; }
        var respText = encoding.GetString(respBytes);

        using var doc = JsonDocument.Parse(respText);
        var root = doc.RootElement;
        var responseKey = method.Replace('.', '_') + "_response";
        if (!root.TryGetProperty(responseKey, out var bizResp))
        {
            if (root.TryGetProperty("error_response", out var errResp))
                throw new AlipayOAuthException("ALIPAY_GATEWAY_ERROR", errResp.GetRawText());
            throw new AlipayOAuthException("UNKNOWN_RESPONSE", respText);
        }

        // Response signature verification (guards against tampering).
        var sign = root.TryGetProperty("sign", out var signEl) ? signEl.GetString() : null;
        if (!string.IsNullOrEmpty(sign) && !string.IsNullOrEmpty(platformPublicKeyPem))
        {
            var bizRespRaw = ExtractResponseRaw(respText, responseKey) ?? bizResp.GetRawText();
            if (!VerifyWithRsa2(bizRespRaw, sign, platformPublicKeyPem, encoding))
                throw new AlipayOAuthException("SIGN_VERIFY_FAILED", "Alipay response signature verification failed (possible tampering)");
        }

        if (bizResp.TryGetProperty("code", out var codeEl) && !string.Equals(codeEl.GetString(), "10000", System.StringComparison.Ordinal))
        {
            var code = codeEl.GetString() ?? "";
            var msg = bizResp.TryGetProperty("msg", out var m) ? m.GetString() ?? "" : "";
            var subCode = bizResp.TryGetProperty("sub_code", out var sc) ? sc.GetString() ?? "" : "";
            var subMsg = bizResp.TryGetProperty("sub_msg", out var sm) ? sm.GetString() ?? "" : "";
            throw new AlipayOAuthException(code, $"{msg} | {subCode}: {subMsg}");
        }

        return bizResp.Clone();
    }
}

/// <summary>Alipay OAuth failure carrying a vendor/internal error code.</summary>
/// <summary>Alipay API error.</summary>
public sealed class AlipayOAuthException : Exception
{
    /// <summary>Creates the exception with the provider error code.</summary>
    public AlipayOAuthException(string code, string message) : base(message) => Code = code;
    /// <summary>The provider error code.</summary>
    public string Code { get; }
}
