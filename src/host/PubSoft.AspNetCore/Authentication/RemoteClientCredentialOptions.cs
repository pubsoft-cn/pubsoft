using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Domain;

namespace PubSoft.AspNetCore.Authentication;


/// <summary>
/// Options for <see cref="RemoteClientCredentialVerifier"/>. The endpoint is a deployment-trusted
/// credential registry; plaintext transports are rejected at validation unless loopback or an
/// explicit opt-in is set.
/// </summary>
public sealed class RemoteClientCredentialOptions
{
    /// <summary>
    /// Absolute URI of the remote verification endpoint. Must be https; http is permitted only
    /// for loopback hosts or when <see cref="AllowInsecureEndpoint"/> is set (isolated dev).
    /// </summary>
    public Uri? Endpoint { get; set; }

    /// <summary>Permits a non-https <see cref="Endpoint"/> outside loopback. Keep false in production.</summary>
    public bool AllowInsecureEndpoint { get; set; }

    /// <summary>Per-request timeout (1-15 seconds, default 5). A timed-out verification fails closed.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Result cache minutes (0-60, default 0 = disabled). Positive and negative results share the
    /// TTL -- enabling the cache also delays propagation of registry-side revocations and rotations.
    /// </summary>
    public int CacheMinutes { get; set; }

    internal void Validate()
    {
        ArgumentNullException.ThrowIfNull(Endpoint);
        if (!Endpoint.IsAbsoluteUri)
            throw new ArgumentException("Remote credential endpoint must be an absolute URI.", nameof(Endpoint));
        if (!string.Equals(Endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            && !IsLoopbackHost(Endpoint.Host)
            && !AllowInsecureEndpoint)
        {
            throw new ArgumentException(
                "Remote credential endpoint must be https (loopback hosts are permitted for development); " +
                "set AllowInsecureEndpoint = true to allow plaintext endpoints on other hosts.", nameof(Endpoint));
        }
        if (Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromSeconds(15))
            throw new ArgumentOutOfRangeException(nameof(Timeout), "Timeout must be positive and no longer than 15 seconds.");
        if (CacheMinutes < 0 || CacheMinutes > 60)
            throw new ArgumentOutOfRangeException(nameof(CacheMinutes), "CacheMinutes must be between 0 and 60.");
    }

    private static bool IsLoopbackHost(string host)
        => host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
           || host is "127.0.0.1" or "::1" or "[::1]";
}
