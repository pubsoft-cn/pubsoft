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
