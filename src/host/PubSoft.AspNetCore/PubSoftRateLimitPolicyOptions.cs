using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PubSoft.Security;

namespace PubSoft.AspNetCore;


public sealed class PubSoftRateLimitPolicyOptions
{
    public string Name { get; set; } = string.Empty;
    public int RequestsPerWindow { get; set; } = 100;
    public TimeSpan WindowSize { get; set; } = TimeSpan.FromMinutes(1);
    public int MaxIdentifiers { get; set; } = 100_000;
    public bool CountFailuresOnly { get; set; }
    public Func<HttpContext, bool> AppliesTo { get; set; } = static _ => true;
    public Func<HttpContext, string?> IdentifierSelector { get; set; } = static context =>
        IpNormalizer.Normalize(context.Connection.RemoteIpAddress?.ToString());
    public Func<HttpContext, int, bool> IsFailureResponse { get; set; } = static (_, statusCode) => statusCode >= 400;
}
