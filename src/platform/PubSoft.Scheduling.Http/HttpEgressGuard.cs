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
            return b[0] switch
            {
                0xff => true,                                  // ff00::/8 multicast
                0xfc or 0xfd => true,                          // fc00::/7 unique-local
                0xfe => (b[1] & 0xc0) == 0x80,                 // fe80::/10 link-local
                0x20 => b[1] == 0x02                           // 2002::/16 6to4
                        || b[1] == 0x01 && (b[2], b[3]) is (0, 0) or (0x0d, 0xb8), // Teredo / doc
                _ => false,
            };
        }

        return true;
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
