using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PubSoft.Auditing;

namespace PubSoft.AspNetCore;


public static class AuditMiddlewareExtensions
{
    public static IApplicationBuilder UsePubSoftAudit(
        this IApplicationBuilder app,
        AuditMiddlewareOptions? options = null)
        => app.UseMiddleware<AuditMiddleware>(options);
}
