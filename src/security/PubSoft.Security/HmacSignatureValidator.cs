using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PubSoft.Security;


/// <summary>Validates HMAC-SHA256 requests and atomically rejects nonce replays.</summary>
public sealed class HmacSignatureValidator
{
    private readonly INonceStore _nonceStore;
    private readonly HmacSignatureOptions _options;
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    public HmacSignatureValidator(
        INonceStore nonceStore,
        HmacSignatureOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _nonceStore = nonceStore ?? throw new ArgumentNullException(nameof(nonceStore));
        options ??= new HmacSignatureOptions();
        ValidateOptions(options);
        _options = new HmacSignatureOptions
        {
            MaxTimestampSkewSeconds = options.MaxTimestampSkewSeconds,
            MaxAppIdLength = options.MaxAppIdLength,
            MaxNonceLength = options.MaxNonceLength
        };
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async ValueTask<HmacSignatureResult> ValidateAsync(
        string? appId,
        string? signature,
        string? timestamp,
        string? nonce,
        string? method,
        string? path,
        string? queryString,
        string? bodyHash,
        string? secret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appId) || appId.Length > _options.MaxAppIdLength)
            return Invalid("invalid_app_id");
        if (string.IsNullOrWhiteSpace(signature))
            return Invalid("missing_signature");
        if (string.IsNullOrWhiteSpace(timestamp) || timestamp.Length > 20 ||
            !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var timestampSeconds))
            return Invalid("invalid_timestamp");
        if (string.IsNullOrWhiteSpace(nonce) || nonce.Length > _options.MaxNonceLength)
            return Invalid("invalid_nonce");
        if (string.IsNullOrWhiteSpace(method) || string.IsNullOrWhiteSpace(path))
            return Invalid("invalid_request");
        if (string.IsNullOrEmpty(secret))
            return Invalid("missing_secret");

        var now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        if (timestampSeconds <= now - _options.MaxTimestampSkewSeconds ||
            timestampSeconds > now + _options.MaxTimestampSkewSeconds)
            return Invalid("timestamp_expired");

        if (!TryParseSignature(signature, out var suppliedSignature))
            return Invalid("invalid_signature");

        var stringToSign = BuildStringToSign(method, path, queryString, timestamp, nonce, bodyHash);
        var expectedSignature = ComputeHmacSha256Bytes(secret, stringToSign);
        var signatureMatches = CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature);
        CryptographicOperations.ZeroMemory(suppliedSignature);
        CryptographicOperations.ZeroMemory(expectedSignature);
        if (!signatureMatches)
            return Invalid("signature_mismatch");

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(timestampSeconds)
            .AddSeconds(_options.MaxTimestampSkewSeconds);
        var nonceAccepted = await _nonceStore.TryUseAsync(appId, nonce, expiresAt, cancellationToken).ConfigureAwait(false);
        return nonceAccepted
            ? new HmacSignatureResult { IsValid = true, AppId = appId }
            : Invalid("nonce_replayed");
    }

    public static string ComputeHmacSha256(string key, string data)
        => Convert.ToHexString(ComputeHmacSha256Bytes(key, data)).ToLowerInvariant();

    public static string ComputeBodyHash(Stream body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!body.CanSeek)
            throw new ArgumentException("The request body stream must be seekable.", nameof(body));

        var originalPosition = body.Position;
        try
        {
            body.Position = 0;
            return Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
        }
        finally
        {
            body.Position = originalPosition;
        }
    }

    public static string ComputeBodyHash(byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
    }

    public static string BuildSignature(
        string secret,
        string method,
        string path,
        string timestamp,
        string nonce,
        string? queryString = null,
        string? bodyHash = null)
        => ComputeHmacSha256(secret, BuildStringToSign(method, path, queryString, timestamp, nonce, bodyHash));

    /// <summary>
    /// 规范化签名串：query 以 "?" 开头的原始串（含 '?'）按原样入签 — 签名约束 query 参数，
    /// 防止已签名请求的查询参数被篡改。空 query 统一写 ""（不占行位）。
    /// </summary>
    private static string BuildStringToSign(string method, string path, string? queryString, string timestamp, string nonce, string? bodyHash)
        => $"{method}\n{path}{queryString ?? ""}\n{timestamp}\n{nonce}\n{bodyHash ?? ""}";

    private static byte[] ComputeHmacSha256Bytes(string key, string data)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(data);
        var keyBytes = Encoding.UTF8.GetBytes(key);
        var dataBytes = Encoding.UTF8.GetBytes(data);
        try
        {
            return HMACSHA256.HashData(keyBytes, dataBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyBytes);
            CryptographicOperations.ZeroMemory(dataBytes);
        }
    }

    private static bool TryParseSignature(string signature, out byte[] bytes)
    {
        bytes = [];
        if (signature.Length != 64)
            return false;
        try
        {
            bytes = Convert.FromHexString(signature);
            return bytes.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void ValidateOptions(HmacSignatureOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxTimestampSkewSeconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxAppIdLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxNonceLength);
    }

    private static HmacSignatureResult Invalid(string error) => new() { Error = error };
}
