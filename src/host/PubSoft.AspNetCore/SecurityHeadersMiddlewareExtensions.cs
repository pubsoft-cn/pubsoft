using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace PubSoft.AspNetCore;


public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UsePubSoftSecurityHeaders(
        this IApplicationBuilder app,
        SecurityHeadersOptions? options = null)
        => options is null
            ? app.UseMiddleware<SecurityHeadersMiddleware>()
            : app.UseMiddleware<SecurityHeadersMiddleware>(options);
}
