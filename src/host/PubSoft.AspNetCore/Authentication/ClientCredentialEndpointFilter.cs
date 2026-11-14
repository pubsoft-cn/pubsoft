using Microsoft.AspNetCore.Http;

namespace PubSoft.AspNetCore.Authentication;


/// <summary>Minimal-API filter enforcing a verified client credential.</summary>
public sealed class ClientCredentialEndpointFilter : IEndpointFilter
{
    /// <inheritdoc />
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
