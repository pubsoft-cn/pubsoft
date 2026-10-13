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

public sealed class PermissionEndpointFilter(
    IAuthenticatedSubjectAccessor subjectAccessor,
    ITenantContext tenantContext,
    PermissionAuthorizationService authorizationService) : IEndpointFilter
{
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

public sealed class PermissionMvcAuthorizationFilter(
    IAuthenticatedSubjectAccessor subjectAccessor,
    ITenantContext tenantContext,
    PermissionAuthorizationService authorizationService) : IAsyncAuthorizationFilter
{
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

public static class PermissionMvcAuthorizationExtensions
{
    public static IMvcBuilder AddPubSoftPermissionAuthorization(this IMvcBuilder mvcBuilder)
    {
        ArgumentNullException.ThrowIfNull(mvcBuilder);
        mvcBuilder.Services.TryAddScoped<PermissionMvcAuthorizationFilter>();
        return mvcBuilder.AddMvcOptions(options => options.Filters.AddService<PermissionMvcAuthorizationFilter>());
    }
}

internal static class PermissionRequirementEvaluator
{
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

public static class PermissionEndpointFilterExtensions
{
    public static RouteHandlerBuilder AddPubSoftPermissionAuthorization(this RouteHandlerBuilder endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.AddEndpointFilter<PermissionEndpointFilter>();
    }

    public static RouteGroupBuilder AddPubSoftPermissionAuthorization(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group.AddEndpointFilter<PermissionEndpointFilter>();
    }
}