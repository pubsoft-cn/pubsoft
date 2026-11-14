using Microsoft.AspNetCore.Http;

namespace PubSoft.AspNetCore;


/// <summary>Emits the configured response security headers.</summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SecurityHeadersOptions _options;

    /// <summary>Creates the middleware.</summary>
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

    /// <summary>Sets headers then forwards.</summary>
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
