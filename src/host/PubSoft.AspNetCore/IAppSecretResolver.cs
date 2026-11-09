using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PubSoft.Security;

namespace PubSoft.AspNetCore;

/// <summary>Host seam resolving the shared secret for an app id.</summary>
public interface IAppSecretResolver
{
    /// <summary>Returns the secret for <paramref name="appId"/>, or null when unknown (fail-closed).</summary>
    ValueTask<string?> ResolveSecretAsync(string appId, CancellationToken cancellationToken = default);
}

/// <summary>Validates request signatures against the host-resolved app secret.</summary>
public sealed class HmacValidationMiddleware
{
    private const int MaximumAllowedBodyBytes = 16 * 1024 * 1024;
    private readonly RequestDelegate _next;
    private readonly IAppSecretResolver _secretResolver;
    private readonly HmacSignatureValidator _validator;
    private readonly HmacValidationOptions _options;

    /// <summary>Creates the middleware.</summary>
    public HmacValidationMiddleware(
        RequestDelegate next,
        IAppSecretResolver secretResolver,
        HmacSignatureValidator validator,
        IOptions<HmacValidationOptions> options)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _secretResolver = secretResolver ?? throw new ArgumentNullException(nameof(secretResolver));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(_options.MaximumBodyBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(_options.MaximumBodyBytes, MaximumAllowedBodyBytes);
    }

    /// <summary>Verifies the signature then forwards.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (_options.ExcludedPathPrefixes.Any(prefix => IsExcludedPath(path, prefix)))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (!TryReadSingleHeader(context, _options.AppIdHeader, out var appId) ||
            !TryReadSingleHeader(context, _options.SignatureHeader, out var signature) ||
            !TryReadSingleHeader(context, _options.TimestampHeader, out var timestamp) ||
            !TryReadSingleHeader(context, _options.NonceHeader, out var nonce))
        {
            await Reject(context, StatusCodes.Status401Unauthorized, "missing_or_ambiguous_signature_headers").ConfigureAwait(false);
            return;
        }

        string? secret;
        try
        {
            secret = await _secretResolver.ResolveSecretAsync(appId, context.RequestAborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            await Reject(context, StatusCodes.Status503ServiceUnavailable, "signature_validation_unavailable").ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrEmpty(secret))
        {
            // 与签名不匹配返回同一错误码--避免 appId 存在性枚举 oracle
            await Reject(context, StatusCodes.Status401Unauthorized, "signature_mismatch").ConfigureAwait(false);
            return;
        }

        string? bodyHash = null;
        if (_options.RequireBodyHash)
        {
            if (context.Request.ContentLength > _options.MaximumBodyBytes)
            {
                await Reject(context, StatusCodes.Status413PayloadTooLarge, "request_body_too_large").ConfigureAwait(false);
                return;
            }

            try
            {
                bodyHash = await ComputeBodyHashAsync(context.Request, _options.MaximumBodyBytes, context.RequestAborted).ConfigureAwait(false);
            }
            catch (IOException)
            {
                await Reject(context, StatusCodes.Status413PayloadTooLarge, "request_body_too_large").ConfigureAwait(false);
                return;
            }
        }

        var result = await _validator.ValidateAsync(
            appId,
            signature,
            timestamp,
            nonce,
            context.Request.Method,
            path,
            context.Request.QueryString.Value,
            bodyHash,
            secret,
            context.RequestAborted).ConfigureAwait(false);

        if (!result.IsValid)
        {
            await Reject(context, StatusCodes.Status401Unauthorized, result.Error ?? "invalid_signature").ConfigureAwait(false);
            return;
        }

        context.Items["PubSoft.AppId"] = appId;
        await _next(context).ConfigureAwait(false);
    }

    /// <summary>Segment-boundary-aware prefix match -- "/api" exempts "/api" and "/api/..." but not "/apixyz".</summary>
    private static bool IsExcludedPath(string path, string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return false;
        if (path.Equals(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        return prefix[^1] == '/' || path.Length > prefix.Length && path[prefix.Length] == '/';
    }

    private static async Task<string> ComputeBodyHashAsync(HttpRequest request, int maximumBytes, CancellationToken cancellationToken)
    {
        request.EnableBuffering(bufferThreshold: Math.Min(maximumBytes, 30_720), bufferLimit: maximumBytes);
        var originalPosition = request.Body.Position;
        request.Body.Position = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        var totalBytes = 0;
        try
        {
            int bytesRead;
            while ((bytesRead = await request.Body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                totalBytes += bytesRead;
                if (totalBytes > maximumBytes)
                    throw new InvalidDataException();
                hash.AppendData(buffer, 0, bytesRead);
            }

            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            request.Body.Position = originalPosition;
        }
    }

    private static bool TryReadSingleHeader(HttpContext context, string name, out string value)
    {
        value = string.Empty;
        if (!context.Request.Headers.TryGetValue(name, out var values) || values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
            return false;

        value = values[0]!;
        return true;
    }

    private static async Task Reject(HttpContext context, int statusCode, string error)
    {
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new { error }, context.RequestAborted).ConfigureAwait(false);
    }
}
