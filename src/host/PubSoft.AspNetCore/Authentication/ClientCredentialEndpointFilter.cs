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
