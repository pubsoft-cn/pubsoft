using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PubSoft.Auditing;

namespace PubSoft.AspNetCore;

public sealed class AuditMiddlewareOptions
{
    public bool CaptureClientIp { get; set; }
    public bool CaptureUserAgent { get; set; }
    public bool CaptureActor { get; set; }
}
