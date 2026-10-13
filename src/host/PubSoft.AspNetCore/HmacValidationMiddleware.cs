using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PubSoft.Security;

namespace PubSoft.AspNetCore;

public interface IAppSecretResolver
{
    ValueTask<string?> ResolveSecretAsync(string appId, CancellationToken cancellationToken = default);
}

public sealed class HmacValidationOptions
{
    public string[] ExcludedPathPrefixes { get; set; } = [];
    public bool RequireBodyHash { get; set; }
    public int MaximumBodyBytes { get; set; } = 1_048_576;
}

public static class HmacValidationServiceCollectionExtensions
{
    public static IServiceCollection AddPubSoftHmacValidation(
        this IServiceCollection services,
        Action<HmacValidationOptions>? configure = null,
        Action<HmacSignatureOptions>? configureSignature = null)
    {
        services.TryAddSingleton<INonceStore, MemoryNonceStore>();
        services.AddSingleton(sp =>
        {
            var options = new HmacSignatureOptions();
            configureSignature?.Invoke(options);
            return new HmacSignatureValidator(sp.GetRequiredService<INonceStore>(), options);
        });
        services.Configure(configure ?? (_ => { }));
        return services;
    }

    public static IApplicationBuilder UsePubSoftHmacValidation(this IApplicationBuilder app)
        => app.UseMiddleware<HmacValidationMiddleware>();
}

public sealed class HmacValidationMiddleware
{
    private const int MaximumAllowedBodyBytes = 16 * 1024 * 1024;
    private readonly RequestDelegate _next;
    private readonly IAppSecretResolver _secretResolver;
    private readonly HmacSignatureValidator _validator;
    private readonly HmacValidationOptions _options;

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

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (_options.ExcludedPathPrefixes.Any(prefix =>
                !string.IsNullOrEmpty(prefix) && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (!TryReadSingleHeader(context, HmacSignatureHeaders.AppId, out var appId) ||
            !TryReadSingleHeader(context, HmacSignatureHeaders.Signature, out var signature) ||
            !TryReadSingleHeader(context, HmacSignatureHeaders.Timestamp, out var timestamp) ||
            !TryReadSingleHeader(context, HmacSignatureHeaders.Nonce, out var nonce))
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
            await Reject(context, StatusCodes.Status401Unauthorized, "invalid_app_id").ConfigureAwait(false);
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