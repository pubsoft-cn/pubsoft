using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PubSoft.Auditing;

namespace PubSoft.AspNetCore;


/// <summary>Registration for the audit middleware.</summary>
public static class AuditMiddlewareExtensions
{
    /// <summary>Adds the audit middleware.</summary>
    public static IApplicationBuilder UsePubSoftAudit(
        this IApplicationBuilder app,
        AuditMiddlewareOptions? options = null)
        => options is null
            ? app.UseMiddleware<AuditMiddleware>()
            : app.UseMiddleware<AuditMiddleware>(options);
}
