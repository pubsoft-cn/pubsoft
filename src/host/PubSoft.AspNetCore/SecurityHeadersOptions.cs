using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace PubSoft.AspNetCore;

/// <summary>Response security-header values emitted by the middleware.</summary>
public sealed class SecurityHeadersOptions
{
    /// <summary>X-Content-Type-Options value.</summary>
    public string XContentTypeOptions { get; set; } = "nosniff";
    /// <summary>X-Frame-Options value.</summary>
    public string XFrameOptions { get; set; } = "DENY";
    /// <summary>Referrer-Policy value.</summary>
    public string ReferrerPolicy { get; set; } = "strict-origin-when-cross-origin";
    /// <summary>Content-Security-Policy value.</summary>
    public string ContentSecurityPolicy { get; set; } = "default-src 'self'";
    /// <summary>Permissions-Policy value.</summary>
    public string PermissionsPolicy { get; set; } = "geolocation=(), microphone=(), camera=()";
    /// <summary>Strict-Transport-Security value; null disables the header.</summary>
    public string? StrictTransportSecurity { get; set; } = "max-age=31536000; includeSubDomains";
    /// <summary>Whether security headers are emitted.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Emit HSTS even in Development.</summary>
    public bool EnableHstsInDevelopment { get; set; }
}
