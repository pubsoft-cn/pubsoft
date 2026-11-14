namespace PubSoft.AspNetCore;

/// <summary>
/// Options for <see cref="PubSoftForwardedHeadersExtensions.UsePubSoftForwardedHeaders"/>
/// and <see cref="PubSoftForwardedHeadersExtensions.AddPubSoftForwardedHeaders"/>.
/// </summary>
/// <remarks>
/// Forwarded headers are client-supplied input -- trusting them is a deployment decision.
/// Only enable processing when the edge proxy is configured to overwrite
/// <c>X-Forwarded-*</c> headers, and the backend is either bound to loopback/private
/// network or its peer set is pinned via <see cref="KnownProxies"/>/<see cref="KnownNetworks"/>.
/// </remarks>
public sealed class PubSoftForwardedHeadersOptions
{
    /// <summary>Process X-Forwarded-For (RemoteIpAddress rewrite). Default true.</summary>
    public bool ForwardedFor { get; set; } = true;

    /// <summary>Process X-Forwarded-Proto (Request.Scheme rewrite). Default true.</summary>
    public bool ForwardedProto { get; set; } = true;

    /// <summary>
    /// Process X-Real-IP (RemoteIpAddress rewrite) when the request carries no
    /// X-Forwarded-For and the socket peer is trusted. Default false -- X-Real-IP is a
    /// non-standard single header that the platform ForwardedHeadersOptions cannot
    /// express; enable only when the edge proxy is known to overwrite it (e.g. nginx
    /// <c>proxy_set_header X-Real-IP</c>). Requires
    /// <see cref="PubSoftForwardedHeadersExtensions.UsePubSoftForwardedHeaders"/>.
    /// </summary>
    public bool RealIp { get; set; }

    /// <summary>Max XFF entries consumed from the right. Null = process all entries.</summary>
    public int? ForwardLimit { get; set; }

    /// <summary>
    /// Trust forwarded headers from any upstream peer (clears KnownProxies/KnownNetworks).
    /// Only safe when the backend socket is reachable exclusively from the trusted proxy --
    /// loopback-bound port or private network segment. On directly reachable backends this
    /// lets any client spoof its source IP (rate-limit evasion, audit poisoning).
    /// Default false: only the platform defaults (loopback) are trusted unless
    /// <see cref="KnownProxies"/>/<see cref="KnownNetworks"/> are populated.
    /// </summary>
    public bool TrustAnyProxy { get; set; }

    /// <summary>Trusted proxy addresses (IPv4/IPv6 strings). Invalid entries fail startup.</summary>
    public IList<string> KnownProxies { get; } = new List<string>();

    /// <summary>Trusted proxy networks (CIDR notation, e.g. "172.16.0.0/12"). Invalid entries fail startup.</summary>
    public IList<string> KnownNetworks { get; } = new List<string>();
}
