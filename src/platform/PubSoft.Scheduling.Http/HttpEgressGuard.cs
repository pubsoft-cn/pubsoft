using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace PubSoft.Scheduling.Http;

/// <summary>
/// Egress guard for the scheduled HTTP executor. Enforcement happens at connect
/// time (<see cref="ConnectBlockingAsync(SocketsHttpConnectionContext, CancellationToken)"/>) so
/// DNS answers cannot be swapped between validation and connect (rebinding);
/// <c>TryReadConfig</c> does a cheaper literal/allowlist check for early rejection.
/// </summary>
[PubSoft.Core.BoundedCost("static CIDR table built once at type init — not per-call")]
internal static class HttpEgressGuard
{
    /// <summary>
    /// Host allowlist match. Entries: exact host (<c>api.example.com</c>) or suffix
    /// wildcard (<c>.example.com</c> / <c>*.example.com</c> — matches subdomains AND
    /// the bare domain). An empty list means no allowlist.
    /// </summary>
    public static bool IsHostAllowed(string host, IList<string> allowedHosts)
    {
        foreach (var raw in allowedHosts)
        {
            var entry = raw.StartsWith("*.", StringComparison.Ordinal) ? raw[1..] : raw;
            if (entry.StartsWith('.'))
            {
                if (host.EndsWith(entry, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(host, entry[1..], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (string.Equals(host, entry, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return allowedHosts.Count == 0;
    }

    /// <summary>
    /// Literal-IP check for early rejection inside <see cref="HttpExecutionSupport.TryReadConfig"/>.
    /// DNS names pass — they are enforced at connect time.
    /// </summary>
    public static bool IsBlockedHostLiteral(Uri uri)
        => IPAddress.TryParse(uri.Host, out var address) && IsBlockedAddress(address);

    /// <summary>
    /// Non-public destinations: RFC1918, loopback, link-local (incl. cloud metadata
    /// 169.254.x.x), CGNAT, multicast, reserved/broadcast, documentation and
    /// transition ranges, IPv4-mapped IPv6. Anything unrecognized fails closed.
    /// </summary>
    public static bool IsBlockedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] switch
            {
                0 => true,                                     // this-network
                10 => true,                                    // RFC1918
                100 => b[1] is >= 64 and <= 127,               // CGNAT shared space
                127 => true,                                   // loopback
                169 => b[1] == 254,                            // link-local / cloud metadata
                172 => b[1] is >= 16 and <= 31,                // RFC1918
                192 => b[1] == 168 || b[1] == 0 && b[2] is 0 or 2, // RFC1918 / proto-assign / TEST-NET-1
                198 => b[1] is 18 or 19 || b[1] == 51 && b[2] == 100, // benchmark / TEST-NET-2
                203 => b[1] == 0 && b[2] == 113,               // TEST-NET-3
                >= 224 => true,                                // multicast, reserved, broadcast
                _ => false,
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            if (b.All(x => x == 0))                            // :: unspecified
                return true;
            foreach (var (prefix, bits) in BlockedV6Prefixes)
                if (IsInPrefix(b, prefix, bits))
                    return true;
            return false;
        }

        return true;
    }

    /// <summary>IANA IPv6 special-purpose ranges that are not globally routable.</summary>
    private static readonly (byte[] Prefix, int Bits)[] BlockedV6Prefixes =
    [
        ([0xfc], 7),                                          // fc00::/7 unique-local
        ([0xfe, 0x80], 10),                                   // fe80::/10 link-local
        ([0xff], 8),                                          // ff00::/8 multicast
        ([0x01, 0, 0, 0, 0, 0, 0, 0], 64),                    // 100::/64 discard-only
        ([0, 0x64, 0xff, 0x9b, 0, 1], 48),                    // 64:ff9b:1::/48 v4v6 doc
        ([0x20, 0x01, 0, 0], 32),                             // 2001::/32 Teredo
        ([0x20, 0x01, 0, 0x02, 0, 0], 48),                    // 2001:2::/48 benchmarking
        ([0x20, 0x01, 0, 0x04, 0x01, 0x12], 48),              // 2001:4:112::/48 AS112
        ([0x20, 0x01, 0, 0x10], 28),                          // 2001:10::/28 ORCHID
        ([0x20, 0x01, 0, 0x20], 28),                          // 2001:20::/28 ORCHIDv2
        ([0x20, 0x01, 0, 0x30], 28),                          // 2001:30::/28 DRIP
        ([0x20, 0x01, 0x0d, 0xb8], 32),                       // 2001:db8::/32 documentation
        ([0x20, 0x02], 16),                                   // 2002::/16 6to4
        ([0x3f, 0xff, 0], 20),                                // 3fff::/20 documentation
        ([0x5f, 0x00], 16),                                   // 5f00::/16 SRv6 SIDs
    ];

    private static bool IsInPrefix(byte[] address, byte[] prefix, int bits)
    {
        var whole = bits / 8;
        for (var i = 0; i < whole; i++)
            if (address[i] != prefix[i])
                return false;
        var remainder = bits % 8;
        return remainder == 0
            || (address[whole] & (0xff << (8 - remainder))) == (prefix[whole] & (0xff << (8 - remainder)));
    }

    /// <summary>
    /// <see cref="SocketsHttpHandler.ConnectCallback"/> enforcing
    /// <see cref="IsBlockedAddress"/> at connect time: resolves the host itself,
    /// skips non-public addresses, and connects the socket directly. TLS/SNI still
    /// uses the request host — only the transport target is pinned.
    /// </summary>
    public static ValueTask<Stream> ConnectBlockingAsync(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
        => ConnectBlockingAsync(context.DnsEndPoint.Host, context.DnsEndPoint.Port, cancellationToken);

    /// <summary>Connect-time enforcement extracted for testability — see the callback overload.</summary>
    public static async ValueTask<Stream> ConnectBlockingAsync(
        string host, int port, CancellationToken cancellationToken)
    {
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);

        var anyPublic = false;
        foreach (var address in addresses)
        {
            if (IsBlockedAddress(address))
                continue;
            anyPublic = true;
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(address, port, cancellationToken)
                    .ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException)
            {
                socket.Dispose(); // try the next candidate
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new HttpRequestException(anyPublic
            ? $"egress connect failed for '{host}'"
            : $"egress blocked: '{host}' resolves only to non-public addresses");
    }
}
