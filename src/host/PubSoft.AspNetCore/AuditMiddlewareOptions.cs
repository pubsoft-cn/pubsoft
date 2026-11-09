using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PubSoft.Auditing;

namespace PubSoft.AspNetCore;

/// <summary>Audit middleware capture toggles.</summary>
public sealed class AuditMiddlewareOptions
{
    /// <summary>Capture the client IP.</summary>
    public bool CaptureClientIp { get; set; }
    /// <summary>Capture the user agent.</summary>
    public bool CaptureUserAgent { get; set; }
    /// <summary>Capture the acting subject.</summary>
    public bool CaptureActor { get; set; }
}
