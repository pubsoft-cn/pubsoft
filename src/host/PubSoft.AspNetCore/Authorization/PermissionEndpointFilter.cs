using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Authentication;
using PubSoft.Authorization;
using PubSoft.Domain;

namespace PubSoft.AspNetCore.Authorization;

/// <summary>Minimal-API filter enforcing the required permission before the endpoint runs.</summary>
public sealed class PermissionEndpointFilter(
    IAuthenticatedSubjectAccessor subjectAccessor,
    ITenantContext tenantContext,
    PermissionAuthorizationService authorizationService) : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var metadata = context.HttpContext.GetEndpoint()?.Metadata;
        if (metadata is null)
            return await next(context).ConfigureAwait(false);

        var failureStatusCode = await PermissionRequirementEvaluator.GetFailureStatusCodeAsync(
            metadata,
            subjectAccessor,
            tenantContext,
            authorizationService,
            context.HttpContext.RequestAborted).ConfigureAwait(false);
        if (failureStatusCode is null)
            return await next(context).ConfigureAwait(false);

        return Results.StatusCode(failureStatusCode.Value);
    }
}

/// <summary>MVC authorization filter enforcing the required permission.</summary>
public sealed class PermissionMvcAuthorizationFilter(
    IAuthenticatedSubjectAccessor subjectAccessor,
    ITenantContext tenantContext,
    PermissionAuthorizationService authorizationService) : IAsyncAuthorizationFilter
{
    /// <inheritdoc />
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var failureStatusCode = await PermissionRequirementEvaluator.GetFailureStatusCodeAsync(
            context.ActionDescriptor.EndpointMetadata,
            subjectAccessor,
            tenantContext,
            authorizationService,
            context.HttpContext.RequestAborted).ConfigureAwait(false);
        if (failureStatusCode is not null)
            context.Result = new StatusCodeResult(failureStatusCode.Value);
    }
}

/// <summary>MVC registration helpers for permission authorization.</summary>
public static class PermissionMvcAuthorizationExtensions
{
    /// <summary>Registers the permission authorization filter globally.</summary>
    public static IMvcBuilder AddPubSoftPermissionAuthorization(this IMvcBuilder mvcBuilder)
    {
        ArgumentNullException.ThrowIfNull(mvcBuilder);
        mvcBuilder.Services.TryAddScoped<PermissionMvcAuthorizationFilter>();
        return mvcBuilder.AddMvcOptions(options => options.Filters.AddService<PermissionMvcAuthorizationFilter>());
    }
}

internal static class PermissionRequirementEvaluator
{
    /// <summary>Maps an authorization failure to an HTTP status code.</summary>
    public static async ValueTask<int?> GetFailureStatusCodeAsync(
        IEnumerable<object> metadata,
        IAuthenticatedSubjectAccessor subjectAccessor,
        ITenantContext tenantContext,
        PermissionAuthorizationService authorizationService,
        CancellationToken cancellationToken)
    {
        // Precedence: explicit exemption wins over any permission requirement, including a
        // [PermissionExempt]/[Require*] misconfiguration on the same endpoint. Endpoints without
        // requirement metadata are unaffected. Exemption bypasses subject resolution entirely.
        if (metadata.OfType<PermissionExemptAttribute>().Any() || metadata.OfType<IAllowAnonymous>().Any())
            return null;

        var requiredFlags = metadata.OfType<RequirePermissionAttribute>().ToArray();
        var customPermissions = metadata.OfType<RequireCustomPermissionAttribute>().ToArray();
        var extensionPermissions = metadata.OfType<RequireExtPermissionAttribute>().ToArray();
        if (requiredFlags.Length == 0 && customPermissions.Length == 0 && extensionPermissions.Length == 0)
            return null;

        var subject = subjectAccessor.GetCurrent();
        if (subject is null)
            return StatusCodes.Status401Unauthorized;
        if (subject.TenantId != tenantContext.TenantId)
            return StatusCodes.Status403Forbidden;

        foreach (var requirement in requiredFlags)
        {
            if (!await authorizationService.IsGrantedAsync(
                    subject.SubjectId,
                    subject.TenantId,
                    requirement.ClientCode,
                    requirement.ModuleKey,
                    requirement.Flags,
                    cancellationToken).ConfigureAwait(false))
                return StatusCodes.Status403Forbidden;
        }

        foreach (var requirement in customPermissions)
        {
            if (!await authorizationService.IsCustomPermissionGrantedAsync(
                    subject.SubjectId,
                    subject.TenantId,
                    requirement.ClientCode,
                    requirement.ModuleKey,
                    requirement.PermissionName,
                    cancellationToken).ConfigureAwait(false))
                return StatusCodes.Status403Forbidden;
        }

        foreach (var requirement in extensionPermissions)
        {
            if (!await authorizationService.IsExtensionPermissionGrantedAsync(
                    subject.SubjectId,
                    subject.TenantId,
                    requirement.ClientCode,
                    requirement.ModuleKey,
                    requirement.ExtFlag,
                    cancellationToken).ConfigureAwait(false))
                return StatusCodes.Status403Forbidden;
        }

        return null;
    }
}

/// <summary>Minimal-API registration helpers for permission authorization.</summary>
public static class PermissionEndpointFilterExtensions
{
    /// <summary>Attaches permission authorization to the endpoint.</summary>
    public static RouteHandlerBuilder AddPubSoftPermissionAuthorization(this RouteHandlerBuilder endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.AddEndpointFilter<PermissionEndpointFilter>();
    }

    /// <summary>Attaches permission authorization to the route group.</summary>
    public static RouteGroupBuilder AddPubSoftPermissionAuthorization(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group.AddEndpointFilter<PermissionEndpointFilter>();
    }
}