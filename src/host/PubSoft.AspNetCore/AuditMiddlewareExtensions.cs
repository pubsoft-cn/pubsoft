using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PubSoft.AspNetCore;


/// <summary>Registration for the audit middleware.</summary>
public static class AuditMiddlewareExtensions
{
    /// <summary>
    /// Adds the audit middleware. Capture flags (<c>CaptureClientIp</c>, <c>CaptureUserAgent</c>,
    /// <c>CaptureActor</c>) default off for privacy; registering with all of them off logs a
    /// startup warning - audit rows then lack the forensic fields.
    /// </summary>
    public static IApplicationBuilder UsePubSoftAudit(
        this IApplicationBuilder app,
        AuditMiddlewareOptions? options = null)
    {
        var effective = options ?? new AuditMiddlewareOptions();
        if (!effective.CaptureClientIp && !effective.CaptureUserAgent && !effective.CaptureActor)
            app.ApplicationServices.GetService<ILoggerFactory>()
                ?.CreateLogger("PubSoft.AspNetCore.AuditMiddleware")
                .LogWarning(
                    "PubSoft audit middleware registered with all capture flags off " +
                    "(CaptureClientIp/CaptureUserAgent/CaptureActor) - audit rows will lack " +
                    "client IP, user agent and actor. Set the flags explicitly to collect " +
                    "forensic fields, or keep them off for a privacy-minimal audit.");
        return options is null
            ? app.UseMiddleware<AuditMiddleware>()
            : app.UseMiddleware<AuditMiddleware>(options);
    }
}
