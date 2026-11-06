using System.Net;
using Microsoft.AspNetCore.Http;

namespace PubSoft.AspNetCore;

/// <summary>
/// Detects the "proxied deployment without forwarded-header processing" misconfiguration:
/// the socket peer is a private/loopback address (typical reverse proxy or container
/// bridge) while an inbound <c>X-Forwarded-For</c>/<c>X-Real-IP</c> header advertises a
/// public client address that was never consumed.
/// <para>
/// In that state every consumer of <c>Connection.RemoteIpAddress</c> sees the same proxy
/// IP -- rate-limit buckets collapse across all clients, audit IPs are all identical, and
/// IP-based bans hit everyone at once. Fix by inserting
/// <see cref="PubSoftForwardedHeadersExtensions.UsePubSoftForwardedHeaders"/> early in
/// the pipeline.
/// </para>
/// </summary>
public static class ProxyDeploymentDiagnostics
{
    /// <summary>
    /// True when the request carries forwarded headers that were not processed:
    /// the advertised client address is public while the socket peer is private/loopback.
    /// Returns false when forwarded headers were already applied (RemoteIpAddress is the
    /// public client), when the peer is already public (direct connection), or when no
    /// forwarded headers are present.
    /// </summary>
    public static bool HasUnprocessedForwardedHeaders(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var remote = context.Connection.RemoteIpAddress;
        if (remote is null || !IsPrivateOrLoopback(remote))
            return false;

        var advertised = FirstAdvertisedAddress(context);
        return advertised is not null && !IsPrivateOrLoopback(advertised);
    }

    private static IPAddress? FirstAdvertisedAddress(HttpContext context)
    {
        var xff = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(xff))
        {
            var first = xff.Split(',')[0].Trim();
            if (IPAddress.TryParse(first, out var parsed))
                return parsed;
        }

        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp) && IPAddress.TryParse(realIp.Trim(), out var parsedReal))
            return parsedReal;

        return null;
    }

    private static bool IsPrivateOrLoopback(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || (b[0] == 169 && b[1] == 254)
                || b[0] == 127;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6UniqueLocal
                || address.IsIPv6Multicast;
        }

        return false;
    }
}
