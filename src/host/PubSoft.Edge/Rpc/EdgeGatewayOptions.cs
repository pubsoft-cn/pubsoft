using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PubSoft.Edge;


/// <summary>
/// Edge gateway options.
/// </summary>
public sealed class EdgeGatewayOptions
{
    public const string SectionName = "Edge:Gateway";

    /// <summary>
    /// Accepted clock skew for signed requests (seconds). 0 disables the check.
    /// Signature schemes (Hmac/Md5) are rejected when |now - timestamp| exceeds this window.
    /// </summary>
    public int TimestampToleranceSeconds { get; set; } = 300;

    /// <summary>
    /// Accept <c>POST {gatewayPath}/{method}</c> pseudo-paths — the path tail overrides
    /// <c>envelope.method</c> before dispatch. Off by default (wire-protocol extension);
    /// enable to let OpenAPI/Swagger Try-it-out and Postman call methods directly.
    /// When enabled, <see cref="EdgeRateLimitMiddleware"/> matches pseudo-paths too.
    /// </summary>
    public bool EnablePseudoPaths { get; set; }
}
