using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace PubSoft.AspNetCore;


/// <summary>Registration for the correlation-id middleware.</summary>
public static class CorrelationIdMiddlewareExtensions
{
    /// <summary>Register correlation-id adoption + echo -- place before any
    /// middleware/logging that should share the id.</summary>
    public static IApplicationBuilder UsePubSoftCorrelationId(
        this IApplicationBuilder app, CorrelationIdOptions? options = null)
        => options is null
            ? app.UseMiddleware<CorrelationIdMiddleware>()
            : app.UseMiddleware<CorrelationIdMiddleware>(options);
}
