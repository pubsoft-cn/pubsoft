using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace PubSoft.AspNetCore;

/// <summary>Client-IP normalization for rate-limit partitioning.</summary>
public static class IpNormalizer
{
    /// <summary>Normalizes an IP literal; IPv4-mapped IPv6 collapses to IPv4, unparseable becomes empty.</summary>
    public static string Normalize(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return string.Empty;

        return IPAddress.TryParse(ip, out var address) && address.IsIPv4MappedToIPv6
            ? address.MapToIPv4().ToString()
            : ip;
    }

    /// <summary>
    /// Resolves the caller's IP honoring X-Forwarded-For (first hop) then X-Real-IP,
    /// falling back to the connection remote address. Headers are only trusted when the
    /// deployment front proxy is configured to set them -- untrusted deployments should
    /// strip inbound XFF/Real-IP at the edge.
    /// </summary>
    public static string GetRemoteIp(HttpContext? context)
    {
        if (context is null)
            return string.Empty;

        var xff = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(xff))
        {
            var normalized = Normalize(xff.Split(',')[0].Trim());
            if (!string.IsNullOrEmpty(normalized))
                return normalized;
        }

        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!StringValues.IsNullOrEmpty(realIp))
            return Normalize(realIp);

        return Normalize(context.Connection.RemoteIpAddress?.ToString());
    }
}