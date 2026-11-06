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

    /// <summary>Creates a validator; <paramref name="nonceStore"/> must be shared across replicas for replay protection to hold.</summary>
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
            MaxNonceLength = options.MaxNonceLength,
            SignatureEncoding = options.SignatureEncoding,
            TimestampUnit = options.TimestampUnit
        };
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Validates a signed request: timestamp skew, signature (fixed-time compare), then atomic nonce reservation.</summary>
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

        var now = _options.TimestampUnit == HmacTimestampUnit.Milliseconds
            ? _timeProvider.GetUtcNow().ToUnixTimeMilliseconds()
            : _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        var skew = _options.TimestampUnit == HmacTimestampUnit.Milliseconds
            ? _options.MaxTimestampSkewSeconds * 1000L
            : (long)_options.MaxTimestampSkewSeconds;
        if (timestampSeconds <= now - skew || timestampSeconds > now + skew)
            return Invalid("timestamp_expired");

        if (!TryParseSignature(signature, _options.SignatureEncoding, out var suppliedSignature))
            return Invalid("invalid_signature");

        var stringToSign = BuildStringToSign(method, path, queryString, timestamp, nonce, bodyHash);
        var expectedSignature = ComputeHmacSha256Bytes(secret, stringToSign);
        var signatureMatches = CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature);
        CryptographicOperations.ZeroMemory(suppliedSignature);
        CryptographicOperations.ZeroMemory(expectedSignature);
        if (!signatureMatches)
            return Invalid("signature_mismatch");

        var expiresAt = (_options.TimestampUnit == HmacTimestampUnit.Milliseconds
                ? DateTimeOffset.FromUnixTimeMilliseconds(timestampSeconds)
                : DateTimeOffset.FromUnixTimeSeconds(timestampSeconds))
            .AddSeconds(_options.MaxTimestampSkewSeconds);
        var nonceAccepted = await _nonceStore.TryUseAsync(appId, nonce, expiresAt, cancellationToken).ConfigureAwait(false);
        return nonceAccepted
            ? new HmacSignatureResult { IsValid = true, AppId = appId }
            : Invalid("nonce_replayed");
    }

    /// <summary>Lowercase-hex HMAC-SHA256 of <paramref name="data"/> under <paramref name="key"/>.</summary>
    public static string ComputeHmacSha256(string key, string data)
        => Convert.ToHexString(ComputeHmacSha256Bytes(key, data)).ToLowerInvariant();

    /// <summary>Lowercase-hex SHA-256 of a seekable body stream; position is preserved.</summary>
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

    /// <summary>Lowercase-hex SHA-256 of a body buffer.</summary>
    public static string ComputeBodyHash(byte[] body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
    }

    /// <summary>Client-side helper: builds the canonical string and returns the HMAC-SHA256 signature for it.</summary>
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
    /// Canonical string-to-sign: the raw query string including its leading "?" is signed verbatim -- the signature pins the query parameters,
    /// so a signed request's query cannot be tampered with. An empty query normalizes to "" (no line slot).
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

    private static bool TryParseSignature(string signature, HmacSignatureEncoding encoding, out byte[] bytes)
    {
        bytes = [];
        try
        {
            bytes = encoding == HmacSignatureEncoding.Base64
                ? Convert.FromBase64String(signature)
                : Convert.FromHexString(signature);
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
