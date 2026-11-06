using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace PubSoft.Authentication.Jwt.AspNetCore;


public static class JwtBearerAuthenticationExtensions
{
    public static AuthenticationBuilder AddPubSoftJwtBearer(
        this AuthenticationBuilder builder,
        Action<PubSoftJwtBearerOptions> configure,
        string scheme = JwtBearerDefaults.AuthenticationScheme)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);

        var options = new PubSoftJwtBearerOptions();
        configure(options);
        ValidateCommonOptions(options);
        var useRemoteMetadata = !string.IsNullOrWhiteSpace(options.MetadataAddress);
        var useStaticJwks = !string.IsNullOrWhiteSpace(options.JwksJson);
        var useSymmetricKey = options.SymmetricKey is { Length: > 0 };
        if ((useRemoteMetadata ? 1 : 0) + (useStaticJwks ? 1 : 0) + (useSymmetricKey ? 1 : 0) != 1)
            throw new ArgumentException(
                "Configure exactly one of static JWKS, remote metadata address, or symmetric key.",
                nameof(configure));

        JsonWebKey[] keys = useStaticJwks ? ParsePublicSigningKeys(options.JwksJson) : [];
        SymmetricSecurityKey? symmetricKey = null;
        if (useSymmetricKey)
        {
            if (options.SymmetricKey!.Length < 32)
                throw new ArgumentException("Symmetric key must contain at least 256 bits.", nameof(options.SymmetricKey));
            symmetricKey = new SymmetricSecurityKey(options.SymmetricKey)
            {
                KeyId = string.IsNullOrWhiteSpace(options.SymmetricKeyId) ? null : options.SymmetricKeyId
            };
        }

        return builder.AddJwtBearer(scheme, bearerOptions =>
        {
            bearerOptions.MapInboundClaims = false;
            bearerOptions.RefreshOnIssuerKeyNotFound = true;
            bearerOptions.BackchannelTimeout = options.BackchannelTimeout;
            if (useRemoteMetadata)
            {
                bearerOptions.MetadataAddress = options.MetadataAddress;
                bearerOptions.RequireHttpsMetadata = options.RequireHttpsMetadata;
            }
            bearerOptions.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = options.Issuer,
                ValidateAudience = true,
                ValidAudience = options.Audience,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ValidateLifetime = true,
                ValidAlgorithms = useSymmetricKey
                    ? [SecurityAlgorithms.HmacSha256]
                    : [SecurityAlgorithms.RsaSha256],
                ClockSkew = options.ClockSkew,
                TryAllIssuerSigningKeys = false,
                NameClaimType = options.NameClaimType,
                RoleClaimType = options.RoleClaimType,
                IssuerSigningKeyResolver = useSymmetricKey
                    ? (_, _, keyId, _) => string.IsNullOrWhiteSpace(keyId)
                        ? [symmetricKey!]
                        : string.Equals(symmetricKey!.KeyId, keyId, System.StringComparison.Ordinal) ? [symmetricKey] : []
                    : useRemoteMetadata
                        ? (_, _, keyId, validationParameters) => ResolveSigningKeys(
                            keyId,
                            validationParameters.IssuerSigningKeys ?? [])
                        : (_, _, keyId, _) => ResolveSigningKeys(keyId, keys)
            };
        });
    }

    private static IEnumerable<SecurityKey> ResolveSigningKeys(string? keyId, IEnumerable<SecurityKey> candidates)
    {
        if (string.IsNullOrWhiteSpace(keyId))
            return [];

        var matches = candidates
            .Where(key => string.Equals(key.KeyId, keyId, System.StringComparison.Ordinal) && IsPublicRsaSigningKey(key))
            .Take(2)
            .ToArray();

        return matches.Length == 1 ? matches : [];
    }

    private static bool IsPublicRsaSigningKey(SecurityKey key)
    {
        if (key is JsonWebKey jwk)
            return string.Equals(jwk.Kty, "RSA", System.StringComparison.Ordinal)
                && (string.IsNullOrEmpty(jwk.Use) || string.Equals(jwk.Use, "sig", System.StringComparison.Ordinal))
                && (string.IsNullOrEmpty(jwk.Alg) || string.Equals(jwk.Alg, SecurityAlgorithms.RsaSha256, System.StringComparison.Ordinal))
                && !string.IsNullOrWhiteSpace(jwk.N)
                && !string.IsNullOrWhiteSpace(jwk.E)
                && jwk.D is null
                && jwk.P is null
                && jwk.Q is null
                && jwk.DP is null
                && jwk.DQ is null
                && jwk.QI is null
                && jwk.Oth is not { Count: > 0 }
                && jwk.KeySize >= 2048;

        if (key is not RsaSecurityKey rsaKey || rsaKey.KeySize < 2048)
            return false;

        try
        {
            var parameters = rsaKey.Rsa?.ExportParameters(true) ?? rsaKey.Parameters;
            return parameters.Modulus is { Length: > 0 }
                && parameters.Exponent is { Length: > 0 }
                && parameters.D is null
                && parameters.P is null
                && parameters.Q is null
                && parameters.DP is null
                && parameters.DQ is null
                && parameters.InverseQ is null;
        }
        catch (CryptographicException)
        {
            return true;
        }
    }

    private static void ValidateCommonOptions(PubSoftJwtBearerOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Audience);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.NameClaimType);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.RoleClaimType);
        if (options.Issuer.Any(char.IsControl) || options.Audience.Any(char.IsControl))
            throw new ArgumentException("JWT issuer and audience cannot contain control characters.");
        if (options.ClockSkew < TimeSpan.Zero || options.ClockSkew > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(options.ClockSkew));
        if (options.BackchannelTimeout <= TimeSpan.Zero || options.BackchannelTimeout > TimeSpan.FromMinutes(2))
            throw new ArgumentOutOfRangeException(nameof(options.BackchannelTimeout));

        if (!string.IsNullOrWhiteSpace(options.MetadataAddress))
        {
            if (!Uri.TryCreate(options.MetadataAddress, UriKind.Absolute, out var metadataUri)
                || !string.IsNullOrEmpty(metadataUri.UserInfo)
                || !string.IsNullOrEmpty(metadataUri.Fragment))
                throw new ArgumentException("Metadata address must be an absolute URL without user info or fragment.", nameof(options.MetadataAddress));
            if (options.RequireHttpsMetadata && !string.Equals(metadataUri.Scheme, Uri.UriSchemeHttps, System.StringComparison.Ordinal))
                throw new ArgumentException("Metadata address must use HTTPS.", nameof(options.MetadataAddress));
            if (!options.RequireHttpsMetadata
                && !string.Equals(metadataUri.Scheme, Uri.UriSchemeHttps, System.StringComparison.Ordinal)
                && !string.Equals(metadataUri.Scheme, Uri.UriSchemeHttp, System.StringComparison.Ordinal))
                throw new ArgumentException("Metadata address must use HTTP or HTTPS.", nameof(options.MetadataAddress));
        }
    }

    private static JsonWebKey[] ParsePublicSigningKeys(string jwksJson)
    {
        if (string.IsNullOrWhiteSpace(jwksJson) || jwksJson.Length > 256 * 1024)
            throw new ArgumentException("A non-empty JWKS document of at most 256 KiB is required.", nameof(jwksJson));

        var keys = new JsonWebKeySet(jwksJson).Keys.ToArray();
        if (keys.Length is 0 or > 64)
            throw new ArgumentException("JWKS must contain between one and 64 signing keys.", nameof(jwksJson));
        if (keys.Any(key => !string.Equals(key.Kty, "RSA", System.StringComparison.Ordinal)
            || !string.Equals(key.Use, "sig", System.StringComparison.Ordinal)
            || !string.Equals(key.Alg, SecurityAlgorithms.RsaSha256, System.StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(key.Kid)
            || string.IsNullOrWhiteSpace(key.N)
            || string.IsNullOrWhiteSpace(key.E)
            || key.D is not null
            || key.P is not null
            || key.Q is not null
            || key.DP is not null
            || key.DQ is not null
            || key.QI is not null
            || key.Oth is { Count: > 0 }))
            throw new ArgumentException("JWKS must contain only public RSA/RS256 signing keys with unique key ids.", nameof(jwksJson));
        if (keys.GroupBy(key => key.Kid, StringComparer.Ordinal).Any(group => group.Skip(1).Any()))
            throw new ArgumentException("JWKS key ids must be unique.", nameof(jwksJson));

        return keys;
    }
}
