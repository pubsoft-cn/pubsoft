using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace PubSoft.AspNetCore;

public sealed class SecurityHeadersOptions
{
    public string XContentTypeOptions { get; set; } = "nosniff";
    public string XFrameOptions { get; set; } = "DENY";
    public string ReferrerPolicy { get; set; } = "strict-origin-when-cross-origin";
    public string ContentSecurityPolicy { get; set; } = "default-src 'self'";
    public string PermissionsPolicy { get; set; } = "geolocation=(), microphone=(), camera=()";
    public string? StrictTransportSecurity { get; set; } = "max-age=31536000; includeSubDomains";
    public bool Enabled { get; set; } = true;
    public bool EnableHstsInDevelopment { get; set; }
}

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SecurityHeadersOptions _options;

    public SecurityHeadersMiddleware(RequestDelegate next, SecurityHeadersOptions? options = null)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _options = options ?? new SecurityHeadersOptions();
        ValidateHeaderValue(_options.XContentTypeOptions, nameof(options));
        ValidateHeaderValue(_options.XFrameOptions, nameof(options));
        ValidateHeaderValue(_options.ReferrerPolicy, nameof(options));
        ValidateHeaderValue(_options.ContentSecurityPolicy, nameof(options));
        ValidateHeaderValue(_options.PermissionsPolicy, nameof(options));
        ValidateHeaderValue(_options.StrictTransportSecurity, nameof(options));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (_options.Enabled)
        {
            SetIfConfigured(context, "X-Content-Type-Options", _options.XContentTypeOptions);
            SetIfConfigured(context, "X-Frame-Options", _options.XFrameOptions);
            SetIfConfigured(context, "Referrer-Policy", _options.ReferrerPolicy);
            SetIfConfigured(context, "Content-Security-Policy", _options.ContentSecurityPolicy);
            SetIfConfigured(context, "Permissions-Policy", _options.PermissionsPolicy);
            if (context.Request.IsHttps || _options.EnableHstsInDevelopment)
                SetIfConfigured(context, "Strict-Transport-Security", _options.StrictTransportSecurity);
        }

        await _next(context).ConfigureAwait(false);
    }

    private static void SetIfConfigured(HttpContext context, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            context.Response.Headers[name] = value;
    }

    private static void ValidateHeaderValue(string? value, string parameterName)
    {
        if (value?.Contains('\r') == true || value?.Contains('\n') == true)
            throw new ArgumentException("Header values cannot contain CR or LF.", parameterName);
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UsePubSoftSecurityHeaders(
        this IApplicationBuilder app,
        SecurityHeadersOptions? options = null)
        => options is null
            ? app.UseMiddleware<SecurityHeadersMiddleware>()
            : app.UseMiddleware<SecurityHeadersMiddleware>(options);
}