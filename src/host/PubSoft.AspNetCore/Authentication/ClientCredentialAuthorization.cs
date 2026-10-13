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

/// <summary>
/// Verifies a machine (service-to-service) credential pair. The host supplies the registry
/// (e.g. a hashed-secret store); the credential format and lookup are entirely host-defined.
/// </summary>
public interface IClientCredentialVerifier
{
    Task<ClientCredentialVerificationResult> VerifyAsync(
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken = default);
}

/// <summary>Outcome of a machine credential check.</summary>
public sealed class ClientCredentialVerificationResult
{
    public bool Success { get; init; }

    /// <summary>Canonical client identifier to attach to the machine principal.</summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// Optional tenant to scope the ambient tenant context to for the remainder of the request
    /// (used when the credential resolves to a tenant-owned client).
    /// </summary>
    public long? TenantId { get; init; }

    /// <summary>Additional claims to attach to the machine principal.</summary>
    public IReadOnlyDictionary<string, string>? Claims { get; init; }

    public static ClientCredentialVerificationResult Fail() => new();
    public static ClientCredentialVerificationResult Ok(string clientId, long? tenantId = null,
        IReadOnlyDictionary<string, string>? claims = null)
        => new() { Success = true, ClientId = clientId, TenantId = tenantId, Claims = claims };
}

/// <summary>
/// Marks an endpoint as accepting machine credentials via <c>X-Client-Id</c>/<c>X-Client-Secret</c>
/// headers. By default only a valid machine credential passes; set <see cref="AllowAuthenticatedUser"/>
/// to also let an already-authenticated principal through.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RequireClientCredentialAttribute : Attribute
{
    /// <summary>Whether an already-authenticated user principal bypasses the credential check.</summary>
    public bool AllowAuthenticatedUser { get; set; }
}

/// <summary>Configurable pieces of machine credential authorization.</summary>
public sealed class ClientCredentialOptions
{
    public string ClientIdHeader { get; set; } = "X-Client-Id";
    public string ClientSecretHeader { get; set; } = "X-Client-Secret";
    public string ClientIdClaimType { get; set; } = "client_id";
    public string CallerTypeClaimType { get; set; } = "caller_type";
    public string CallerTypeMachine { get; set; } = "Machine";
}

public sealed class ClientCredentialMvcAuthorizationFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var evaluation = await ClientCredentialEvaluator
            .EvaluateAsync(context.ActionDescriptor.EndpointMetadata, context.HttpContext)
            .ConfigureAwait(false);
        if (evaluation.FailureStatusCode is not null)
        {
            context.Result = new StatusCodeResult(evaluation.FailureStatusCode.Value);
            return;
        }

        // The tenant scope must wrap next() — ambient (AsyncLocal) changes made before
        // a callee returns never flow back to the caller frame.
        // The tenant scope must be opened in THIS frame and wrap next() — ambient
        // (AsyncLocal) changes made inside an async callee never flow back to the caller.
        using (evaluation.TenantContext?.Change(evaluation.TenantId) ?? NoopDisposable.Instance)
            await next().ConfigureAwait(false);
    }
}

public sealed class ClientCredentialEndpointFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var evaluation = await ClientCredentialEvaluator
            .EvaluateAsync(context.HttpContext.GetEndpoint()?.Metadata, context.HttpContext)
            .ConfigureAwait(false);
        if (evaluation.FailureStatusCode is not null)
            return Results.StatusCode(evaluation.FailureStatusCode.Value);

        using (evaluation.TenantContext?.Change(evaluation.TenantId) ?? NoopDisposable.Instance)
            return await next(context).ConfigureAwait(false);
    }
}

internal static class ClientCredentialEvaluator
{
    internal const string VerifiedClientIdItemKey = "PubSoft.ClientCredential.ClientId";

    public readonly record struct Evaluation(
        int? FailureStatusCode,
        AmbientTenantContext? TenantContext,
        long? TenantId);

    public static async ValueTask<Evaluation> EvaluateAsync(
        IEnumerable<object>? metadata,
        HttpContext httpContext)
    {
        var requirement = metadata?.OfType<RequireClientCredentialAttribute>().FirstOrDefault();
        if (requirement is null)
            return new Evaluation(null, null, null);

        // Fail-closed: without a registered verifier no endpoint marked with the attribute is reachable.
        var verifier = httpContext.RequestServices.GetService<IClientCredentialVerifier>();
        if (verifier is null)
            return new Evaluation(StatusCodes.Status403Forbidden, null, null);

        var options = httpContext.RequestServices.GetService<ClientCredentialOptions>() ?? new ClientCredentialOptions();

        if (requirement.AllowAuthenticatedUser && httpContext.User.Identity?.IsAuthenticated == true)
            return new Evaluation(null, null, null);

        var clientId = httpContext.Request.Headers[options.ClientIdHeader].FirstOrDefault();
        var clientSecret = httpContext.Request.Headers[options.ClientSecretHeader].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return new Evaluation(StatusCodes.Status401Unauthorized, null, null);

        // Identical 401 for unknown/invalid credentials — no client-existence oracle.
        var result = await verifier
            .VerifyAsync(clientId, clientSecret, httpContext.RequestAborted)
            .ConfigureAwait(false);
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
}

internal sealed class NoopDisposable : IDisposable
{
    internal static readonly NoopDisposable Instance = new();
    public void Dispose() { }
}

public static class ClientCredentialAuthorizationExtensions
{
    /// <summary>Registers the MVC action filter enforcing <see cref="RequireClientCredentialAttribute"/>.</summary>
    public static IMvcBuilder AddPubSoftClientCredentialAuthorization(this IMvcBuilder mvcBuilder)
    {
        ArgumentNullException.ThrowIfNull(mvcBuilder);
        mvcBuilder.Services.TryAddScoped<ClientCredentialMvcAuthorizationFilter>();
        return mvcBuilder.AddMvcOptions(options => options.Filters.AddService<ClientCredentialMvcAuthorizationFilter>());
    }

    /// <summary>Adds the machine-credential endpoint filter to a minimal-API route.</summary>
    public static RouteHandlerBuilder AddPubSoftClientCredential(this RouteHandlerBuilder endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.AddEndpointFilter<ClientCredentialEndpointFilter>();
    }

    public static RouteGroupBuilder AddPubSoftClientCredential(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group.AddEndpointFilter<ClientCredentialEndpointFilter>();
    }
}
