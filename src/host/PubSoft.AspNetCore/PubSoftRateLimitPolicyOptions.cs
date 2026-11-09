using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PubSoft.Security;

namespace PubSoft.AspNetCore;


/// <summary>One declarative rate-limit policy: window, partition selector, and failure counting.</summary>
public sealed class PubSoftRateLimitPolicyOptions
{
    /// <summary>Policy name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Permitted requests per window per identifier.</summary>
    public int RequestsPerWindow { get; set; } = 100;
    /// <summary>Window length.</summary>
    public TimeSpan WindowSize { get; set; } = TimeSpan.FromMinutes(1);
    /// <summary>Cap on distinct partition identifiers.</summary>
    public int MaxIdentifiers { get; set; } = 100_000;
    /// <summary>Count only failure responses toward the limit.</summary>
    public bool CountFailuresOnly { get; set; }
    /// <summary>Predicate selecting requests this policy covers.</summary>
    public Func<HttpContext, bool> AppliesTo { get; set; } = static _ => true;
    /// <summary>Partition-identifier selector (defaults to normalized client IP).</summary>
    public Func<HttpContext, string?> IdentifierSelector { get; set; } = static context =>
        IpNormalizer.Normalize(context.Connection.RemoteIpAddress?.ToString());
    /// <summary>Predicate classifying a response as a failure.</summary>
    public Func<HttpContext, int, bool> IsFailureResponse { get; set; } = static (_, statusCode) => statusCode >= 400;
}
