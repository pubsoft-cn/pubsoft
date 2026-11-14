using System.Net;

namespace PubSoft.Core;

/// <summary>
/// IP allow-list matcher over single addresses and CIDR ranges. Entries parse eagerly at
/// construction - a malformed entry is a configuration defect and throws. Empty-list behavior
/// is explicit via <see cref="GateEmptyPolicy"/> (default deny).
/// </summary>
public sealed class IpAllowList
{
    private readonly HashSet<IPAddress> _addresses = new();
    private readonly List<IPNetwork> _networks = [];
    private readonly GateEmptyPolicy _emptyPolicy;

    /// <summary>Creates the matcher from single IPs and/or CIDR entries (<c>"10.0.0.0/8"</c>).</summary>
    /// <exception cref="ArgumentException">An entry is neither a valid IP nor valid CIDR.</exception>
    public IpAllowList(IEnumerable<string>? entries, GateEmptyPolicy emptyPolicy = GateEmptyPolicy.Deny)
    {
        _emptyPolicy = emptyPolicy;
        if (entries is null)
            return;
        foreach (var raw in entries)
        {
            var entry = raw?.Trim();
            if (string.IsNullOrEmpty(entry))
                continue;
            if (entry.Contains('/'))
            {
                if (!IPNetwork.TryParse(entry, out var network))
                    throw new ArgumentException($"IP allow-list entry \"{entry}\" is not valid CIDR notation.", nameof(entries));
                _networks.Add(network);
            }
            else
            {
                if (!IPAddress.TryParse(entry, out var address))
                    throw new ArgumentException($"IP allow-list entry \"{entry}\" is not a valid IP address.", nameof(entries));
                _addresses.Add(Normalize(address));
            }
        }
    }

    /// <summary>Whether the list holds no entries.</summary>
    public bool IsEmpty => _addresses.Count == 0 && _networks.Count == 0;

    /// <summary>Admits <paramref name="address"/> when it matches an entry, or the list is empty under <see cref="GateEmptyPolicy.Allow"/>.</summary>
    public bool IsAllowed(IPAddress? address)
    {
        if (IsEmpty)
            return _emptyPolicy == GateEmptyPolicy.Allow;
        if (address is null)
            return false;
        address = Normalize(address);
        if (_addresses.Contains(address))
            return true;
        foreach (var network in _networks)
            if (network.Contains(address))
                return true;
        return false;
    }

    /// <summary>Admits a textual address; unparseable input is denied.</summary>
    public bool IsAllowed(string? address)
    {
        if (IsEmpty)
            return _emptyPolicy == GateEmptyPolicy.Allow;
        return IPAddress.TryParse(address, out var parsed) && IsAllowed(parsed);
    }

    private static IPAddress Normalize(IPAddress address)
        => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
