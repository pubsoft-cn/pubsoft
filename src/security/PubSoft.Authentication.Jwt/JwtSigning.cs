using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace PubSoft.Authentication.Jwt;

public sealed class RsaSigningKeyMaterial : IDisposable
{
    private bool _disposed;

    internal RsaSigningKeyMaterial(string keyId, RSA rsa)
    {
        KeyId = keyId;
        Rsa = rsa;
        SecurityKey = new RsaSecurityKey(rsa) { KeyId = keyId };
    }

    public string KeyId { get; }
    public RSA Rsa { get; }
    public RsaSecurityKey SecurityKey { get; }

    public void Dispose()
    {
        if (_disposed)
            return;
        Rsa.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Explicitly selected symmetric (HMAC) JWT signing key. This exists for hosts that
/// deliberately run symmetric service-to-service tokens — the algorithm is always an
/// explicit choice and is never a fallback from an asymmetric path.
/// </summary>
public sealed class SymmetricSigningKeyMaterial : IDisposable
{
    private readonly byte[] _keyBytes;
    private bool _disposed;

    internal SymmetricSigningKeyMaterial(string keyId, byte[] keyBytes)
    {
        KeyId = keyId;
        _keyBytes = keyBytes;
        SecurityKey = new SymmetricSecurityKey(keyBytes) { KeyId = keyId };
    }

    public string KeyId { get; }
    public SymmetricSecurityKey SecurityKey { get; }

    public void Dispose()
    {
        if (_disposed)
            return;
        CryptographicOperations.ZeroMemory(_keyBytes);
        _disposed = true;
    }
}

public static class JwtSigning
{
    public static RsaSigningKeyMaterial CreateRsaKey(string keyId, int keySize = 2048)
    {
        ValidateKeyId(keyId);
        if (keySize < 2048 || keySize > 8192 || keySize % 256 != 0)
            throw new ArgumentOutOfRangeException(nameof(keySize), "RSA keys must be between 2048 and 8192 bits in 256-bit increments.");

        return new RsaSigningKeyMaterial(keyId, RSA.Create(keySize));
    }

    public static RsaSigningKeyMaterial ImportFromPem(string keyId, string privateKeyPem)
    {
        ValidateKeyId(keyId);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(privateKeyPem);
            if (rsa.KeySize < 2048)
                throw new CryptographicException("RSA signing keys must be at least 2048 bits.");
            return new RsaSigningKeyMaterial(keyId, rsa);
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    public static string CreateAccessToken(
        RsaSigningKeyMaterial keyMaterial,
        string issuer,
        string audience,
        IReadOnlyDictionary<string, string> claims,
        TimeSpan lifetime,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(keyMaterial);
        ValidateIssuerOrAudience(issuer, nameof(issuer));
        ValidateIssuerOrAudience(audience, nameof(audience));
        ArgumentNullException.ThrowIfNull(claims);
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(lifetime), "JWT lifetime must be positive and no longer than one day.");

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var jwtClaims = claims.Select(pair =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
            ArgumentNullException.ThrowIfNull(pair.Value);
            return new Claim(pair.Key, pair.Value);
        });
        var credentials = new SigningCredentials(keyMaterial.SecurityKey, SecurityAlgorithms.RsaSha256);
        var token = new JwtSecurityToken(
            issuer,
            audience,
            jwtClaims,
            now.UtcDateTime,
            now.Add(lifetime).UtcDateTime,
            credentials);
        token.Header[JwtHeaderParameterNames.Kid] = keyMaterial.KeyId;
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Generates a fresh symmetric (HS256) signing key of at least 256 bits.</summary>
    public static SymmetricSigningKeyMaterial CreateSymmetricKey(string keyId, int keySizeBits = 256)
    {
        ValidateKeyId(keyId);
        if (keySizeBits < 256 || keySizeBits > 1024 || keySizeBits % 8 != 0)
            throw new ArgumentOutOfRangeException(nameof(keySizeBits), "Symmetric signing keys must be between 256 and 1024 bits.");

        return new SymmetricSigningKeyMaterial(keyId, RandomNumberGenerator.GetBytes(keySizeBits / 8));
    }

    /// <summary>Imports an existing symmetric (HS256) key. Keys below 256 bits are rejected.</summary>
    public static SymmetricSigningKeyMaterial ImportSymmetricKey(string keyId, ReadOnlySpan<byte> keyBytes)
    {
        ValidateKeyId(keyId);
        if (keyBytes.Length < 32 || keyBytes.Length > 128)
            throw new ArgumentOutOfRangeException(nameof(keyBytes), "Symmetric signing keys must be between 256 and 1024 bits.");

        return new SymmetricSigningKeyMaterial(keyId, keyBytes.ToArray());
    }

    /// <summary>Imports an existing symmetric (HS256) key from text. Keys below 256 bits are rejected.</summary>
    public static SymmetricSigningKeyMaterial ImportSymmetricKey(string keyId, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return ImportSymmetricKey(keyId, Encoding.UTF8.GetBytes(key));
    }

    /// <summary>Issues an HS256 access token — an explicit algorithm choice, never a fallback.</summary>
    public static string CreateAccessToken(
        SymmetricSigningKeyMaterial keyMaterial,
        string issuer,
        string audience,
        IReadOnlyDictionary<string, string> claims,
        TimeSpan lifetime,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(keyMaterial);
        ValidateIssuerOrAudience(issuer, nameof(issuer));
        ValidateIssuerOrAudience(audience, nameof(audience));
        ArgumentNullException.ThrowIfNull(claims);
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(lifetime), "JWT lifetime must be positive and no longer than one day.");

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow();
        var jwtClaims = claims.Select(pair =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
            ArgumentNullException.ThrowIfNull(pair.Value);
            return new Claim(pair.Key, pair.Value);
        });
        var credentials = new SigningCredentials(keyMaterial.SecurityKey, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer,
            audience,
            jwtClaims,
            now.UtcDateTime,
            now.Add(lifetime).UtcDateTime,
            credentials);
        token.Header[JwtHeaderParameterNames.Kid] = keyMaterial.KeyId;
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static IReadOnlyDictionary<string, string> ValidateAccessToken(
        string token,
        string issuer,
        string audience,
        RsaSigningKeyMaterial keyMaterial,
        TimeProvider? timeProvider = null,
        TimeSpan? clockSkew = null)
    {
        ArgumentNullException.ThrowIfNull(keyMaterial);
        ValidateIssuerOrAudience(issuer, nameof(issuer));
        ValidateIssuerOrAudience(audience, nameof(audience));
        var jwt = ReadTokenAndValidateKid(token, keyMaterial.KeyId, SecurityAlgorithms.RsaSha256);
        var principal = Validate(jwt, token, issuer, audience, [keyMaterial.SecurityKey], SecurityAlgorithms.RsaSha256, timeProvider, clockSkew);
        return ToClaimsDictionary(principal);
    }

    public static IReadOnlyDictionary<string, string> ValidateAccessToken(
        string token,
        string issuer,
        string audience,
        IReadOnlyCollection<RsaSigningKeyMaterial> keys,
        TimeProvider? timeProvider = null,
        TimeSpan? clockSkew = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var jwt = ReadTokenAndValidateKid(token, expectedKeyId: null, SecurityAlgorithms.RsaSha256);
        var keyId = jwt.Header.Kid;
        var matchingKeys = keys.Where(key => key is not null && key.KeyId == keyId).ToArray();
        if (matchingKeys.Length != 1)
            throw new SecurityTokenSignatureKeyNotFoundException("Token key id does not identify exactly one configured signing key.");

        ValidateIssuerOrAudience(issuer, nameof(issuer));
        ValidateIssuerOrAudience(audience, nameof(audience));
        var principal = Validate(jwt, token, issuer, audience, [matchingKeys[0].SecurityKey], SecurityAlgorithms.RsaSha256, timeProvider, clockSkew);
        return ToClaimsDictionary(principal);
    }

    /// <summary>Validates an HS256 token against a single explicit symmetric key.</summary>
    public static IReadOnlyDictionary<string, string> ValidateAccessToken(
        string token,
        string issuer,
        string audience,
        SymmetricSigningKeyMaterial keyMaterial,
        TimeProvider? timeProvider = null,
        TimeSpan? clockSkew = null)
    {
        ArgumentNullException.ThrowIfNull(keyMaterial);
        ValidateIssuerOrAudience(issuer, nameof(issuer));
        ValidateIssuerOrAudience(audience, nameof(audience));
        var jwt = ReadTokenAndValidateKid(token, keyMaterial.KeyId, SecurityAlgorithms.HmacSha256);
        var principal = Validate(jwt, token, issuer, audience, [keyMaterial.SecurityKey], SecurityAlgorithms.HmacSha256, timeProvider, clockSkew);
        return ToClaimsDictionary(principal);
    }

    /// <summary>Validates an HS256 token against a set of symmetric keys selected by kid.</summary>
    public static IReadOnlyDictionary<string, string> ValidateAccessToken(
        string token,
        string issuer,
        string audience,
        IReadOnlyCollection<SymmetricSigningKeyMaterial> keys,
        TimeProvider? timeProvider = null,
        TimeSpan? clockSkew = null)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var jwt = ReadTokenAndValidateKid(token, expectedKeyId: null, SecurityAlgorithms.HmacSha256);
        var keyId = jwt.Header.Kid;
        var matchingKeys = keys.Where(key => key is not null && key.KeyId == keyId).ToArray();
        if (matchingKeys.Length != 1)
            throw new SecurityTokenSignatureKeyNotFoundException("Token key id does not identify exactly one configured signing key.");

        ValidateIssuerOrAudience(issuer, nameof(issuer));
        ValidateIssuerOrAudience(audience, nameof(audience));
        var principal = Validate(jwt, token, issuer, audience, [matchingKeys[0].SecurityKey], SecurityAlgorithms.HmacSha256, timeProvider, clockSkew);
        return ToClaimsDictionary(principal);
    }

    public static JwtJwksDocument ExportJwks(RsaSigningKeyMaterial keyMaterial)
    {
        ArgumentNullException.ThrowIfNull(keyMaterial);
        return ExportJwks([keyMaterial]);
    }

    public static JwtJwksDocument ExportJwks(IReadOnlyCollection<RsaSigningKeyMaterial> keyMaterials)
    {
        ArgumentNullException.ThrowIfNull(keyMaterials);
        var keys = keyMaterials.ToArray();
        if (keys.Any(key => key is null))
            throw new ArgumentException("JWKS keys cannot contain null entries.", nameof(keyMaterials));
        if (keys.Select(key => key.KeyId).Distinct(StringComparer.Ordinal).Count() != keys.Length)
            throw new ArgumentException("JWKS key ids must be unique.", nameof(keyMaterials));

        return new JwtJwksDocument
        {
            Keys = keys.Select(key =>
            {
                var parameters = key.Rsa.ExportParameters(false);
                return new JwtJwksKey
                {
                    Kid = key.KeyId,
                    N = Base64UrlEncoder.Encode(parameters.Modulus!),
                    E = Base64UrlEncoder.Encode(parameters.Exponent!)
                };
            }).ToList()
        };
    }

    private static JwtSecurityToken ReadTokenAndValidateKid(string token, string? expectedKeyId, string validAlgorithm)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new SecurityTokenException("Malformed token.");
        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token))
            throw new SecurityTokenException("Malformed token.");
        var jwt = handler.ReadJwtToken(token);
        if (!string.Equals(jwt.Header.Alg, validAlgorithm, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(jwt.Header.Kid)
            || (expectedKeyId is not null && !string.Equals(jwt.Header.Kid, expectedKeyId, StringComparison.Ordinal)))
            throw new SecurityTokenException($"Token must use {validAlgorithm} and a matching key id.");
        return jwt;
    }

    private static ClaimsPrincipal Validate(
        JwtSecurityToken jwt,
        string token,
        string issuer,
        string audience,
        IEnumerable<SecurityKey> signingKeys,
        string validAlgorithm,
        TimeProvider? timeProvider,
        TimeSpan? clockSkew)
    {
        var skew = clockSkew ?? TimeSpan.FromSeconds(5);
        if (skew < TimeSpan.Zero || skew > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(clockSkew));

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = signingKeys,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ValidAlgorithms = [validAlgorithm],
            ClockSkew = skew,
            LifetimeValidator = (notBefore, expires, _, _) =>
            {
                var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
                return expires.HasValue && expires.Value > now - skew
                    && (!notBefore.HasValue || notBefore.Value <= now + skew);
            }
        };
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        return handler.ValidateToken(token, validationParameters, out _);
    }

    private static IReadOnlyDictionary<string, string> ToClaimsDictionary(ClaimsPrincipal principal) => principal.Claims
        .GroupBy(claim => claim.Type)
        .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);

    private static void ValidateKeyId(string keyId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyId);
        if (keyId.Length > 128 || keyId.Any(char.IsControl))
            throw new ArgumentException("Key ids must be at most 128 non-control characters.", nameof(keyId));
    }

    private static void ValidateIssuerOrAudience(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Any(char.IsControl))
            throw new ArgumentException("Issuer and audience cannot contain control characters.", parameterName);
    }
}

public sealed class JwtJwksDocument
{
    public List<JwtJwksKey> Keys { get; init; } = [];
}

public sealed class JwtJwksKey
{
    public string Kty { get; init; } = "RSA";
    public string Use { get; init; } = "sig";
    public string Kid { get; init; } = string.Empty;
    public string Alg { get; init; } = SecurityAlgorithms.RsaSha256;
    public string N { get; init; } = string.Empty;
    public string E { get; init; } = string.Empty;
}

public static class RefreshTokenGenerator
{
    public static string CreateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return "rt_" + Base64UrlEncoder.Encode(bytes);
    }
}