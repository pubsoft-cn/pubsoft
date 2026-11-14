using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PubSoft.AspNetCore.Authentication;


/// <summary>MVC action filter enforcing a verified client credential.</summary>
public sealed class ClientCredentialMvcAuthorizationFilter : IAsyncActionFilter
{
    /// <inheritdoc />
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

        // The tenant scope must wrap next() - ambient (AsyncLocal) changes made before
        // a callee returns never flow back to the caller frame.
        // The tenant scope must be opened in THIS frame and wrap next() - ambient
        // (AsyncLocal) changes made inside an async callee never flow back to the caller.
        using (evaluation.TenantContext?.Change(evaluation.TenantId) ?? NoopDisposable.Instance)
            await next().ConfigureAwait(false);
    }
}
