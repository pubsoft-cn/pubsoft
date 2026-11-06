using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Domain;

namespace PubSoft.AspNetCore.Authentication;


internal static class ClientCredentialEvaluator
{
    internal const string VerifiedClientIdItemKey = "PubSoft.ClientCredential.ClientId";

    public static async ValueTask<Evaluation> EvaluateAsync(
        IEnumerable<object>? metadata,
        HttpContext httpContext)
    {
        var requirement = metadata?.OfType<RequireClientCredentialAttribute>().FirstOrDefault();
        if (requirement is null)
            return new Evaluation(null, null, null);

        var options = httpContext.RequestServices.GetService<ClientCredentialOptions>() ?? new ClientCredentialOptions();

        if (requirement.AllowAuthenticatedUser && httpContext.User.Identity?.IsAuthenticated == true)
            return new Evaluation(null, null, null);

        var clientId = httpContext.Request.Headers[options.ClientIdHeader].FirstOrDefault();
        var signature = httpContext.Request.Headers[options.SignatureHeader].FirstOrDefault();

        ClientCredentialVerificationResult result;
        if (!string.IsNullOrWhiteSpace(signature))
        {
            // Signed-credential mode: the verifier owns the wire protocol; we only hand over material.
            var signatureVerifier = httpContext.RequestServices.GetService<IClientSignatureVerifier>();
            if (signatureVerifier is null || string.IsNullOrWhiteSpace(clientId))
                return new Evaluation(StatusCodes.Status401Unauthorized, null, null);

            var timestamp = httpContext.Request.Headers[options.TimestampHeader].FirstOrDefault();
            var nonce = httpContext.Request.Headers[options.NonceHeader].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(timestamp) || string.IsNullOrWhiteSpace(nonce))
                return new Evaluation(StatusCodes.Status401Unauthorized, null, null);

            var request = new ClientSignatureRequest(
                clientId, signature, timestamp, nonce,
                httpContext.Request.Method,
                httpContext.Request.Path.Value ?? string.Empty,
                httpContext.Request.QueryString.Value,
                ReadBody: ct => ReadBodyAsync(httpContext.Request, ct));
            result = await signatureVerifier.VerifyAsync(request, httpContext.RequestAborted)
                .ConfigureAwait(false);
        }
        else
        {
            // Fail-closed: plaintext pair mode requires a registered verifier.
            var verifier = httpContext.RequestServices.GetService<IClientCredentialVerifier>();
            if (verifier is null)
                return new Evaluation(StatusCodes.Status403Forbidden, null, null);
            var clientSecret = httpContext.Request.Headers[options.ClientSecretHeader].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
                return new Evaluation(StatusCodes.Status401Unauthorized, null, null);
            result = await verifier
                .VerifyAsync(clientId, clientSecret, httpContext.RequestAborted)
                .ConfigureAwait(false);
        }
        if (!result.Success || string.IsNullOrWhiteSpace(result.ClientId))
            return new Evaluation(StatusCodes.Status401Unauthorized, null, null);

        var claims = new List<Claim>
        {
            new(options.ClientIdClaimType, result.ClientId),
            new(options.CallerTypeClaimType, options.CallerTypeMachine),
        };
        if (result.TenantId is { } tenantId)
            claims.Add(new Claim("tenant_id", tenantId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (result.Claims is { } extra)
            claims.AddRange(extra.Select(pair => new Claim(pair.Key, pair.Value)));

        httpContext.User.AddIdentity(new ClaimsIdentity(claims, "ClientCredential"));
        httpContext.Items[VerifiedClientIdItemKey] = result.ClientId;

        // The ambient tenant scope is opened by the caller (its frame owns the flow into next()).
        // Only the ambient implementation is switchable; hosts with a custom ITenantContext
        // must perform the equivalent scope change inside their verifier.
        var tenantContext = result.TenantId is not null
            ? httpContext.RequestServices.GetService<AmbientTenantContext>()
            : null;
        return new Evaluation(null, tenantContext, result.TenantId);
    }

    private static async ValueTask<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        request.EnableBuffering();
        var originalPosition = request.Body.Position;
        request.Body.Position = 0;
        try
        {
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            return buffer.ToArray();
        }
        finally
        {
            request.Body.Position = originalPosition;
        }
    }

    public readonly record struct Evaluation(
        int? FailureStatusCode,
        AmbientTenantContext? TenantContext,
        long? TenantId);
}
