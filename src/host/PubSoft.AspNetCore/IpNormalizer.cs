using System.Net;

namespace PubSoft.AspNetCore;

public static class IpNormalizer
{
    public static string Normalize(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
            return string.Empty;

        return IPAddress.TryParse(ip, out var address) && address.IsIPv4MappedToIPv6
            ? address.MapToIPv4().ToString()
            : ip;
    }
}