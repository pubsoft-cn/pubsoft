using Microsoft.AspNetCore.Builder;

namespace PubSoft.AspNetCore;


/// <summary>Registration for the security-headers middleware.</summary>
public static class SecurityHeadersMiddlewareExtensions
{
    /// <summary>Adds the security-headers middleware.</summary>
    public static IApplicationBuilder UsePubSoftSecurityHeaders(
        this IApplicationBuilder app,
        SecurityHeadersOptions? options = null)
        => options is null
            ? app.UseMiddleware<SecurityHeadersMiddleware>()
            : app.UseMiddleware<SecurityHeadersMiddleware>(options);
}
