using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PubSoft.AspNetCore;


public static class PubSoftForwardedHeadersExtensions
{
    private static readonly object SocketPeerKey = new();

    /// <summary>
    /// Inserts forwarded-header processing into the pipeline.
    /// Call once in the host before any middleware that consumes
    /// <c>Connection.RemoteIpAddress</c> (rate limiting, authentication, audit).
    /// </summary>
    public static IApplicationBuilder UsePubSoftForwardedHeaders(
        this IApplicationBuilder app,
        Action<PubSoftForwardedHeadersOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        var options = new PubSoftForwardedHeadersOptions();
        configure?.Invoke(options);
        var forwardedHeaders = Apply(options);
        if (!options.RealIp)
            return app.UseForwardedHeaders(forwardedHeaders);

        // ForwardedHeadersMiddleware rewrites RemoteIpAddress and strips the XFF
        // entries it consumed, so the real-IP pass needs the pre-processing socket
        // peer and XFF presence stashed ahead of it.
        return app
            .Use(async (context, next) =>
            {
                context.Items[SocketPeerKey] = new SocketPeerState(
                    context.Connection.RemoteIpAddress,
                    context.Request.Headers.ContainsKey("X-Forwarded-For"));
                await next().ConfigureAwait(false);
            })
            .UseForwardedHeaders(forwardedHeaders)
            .Use(async (context, next) =>
            {
                ApplyRealIpHeader(context, forwardedHeaders, options.TrustAnyProxy);
                await next().ConfigureAwait(false);
            });
    }

    /// <summary>
    /// Service-side counterpart registering <see cref="ForwardedHeadersOptions"/> via
    /// <see cref="IOptions{TOptions}"/> for hosts that call <c>UseForwardedHeaders()</c>
    /// themselves. Prefer <see cref="UsePubSoftForwardedHeaders"/> — one call, no drift.
    /// Throws when <see cref="PubSoftForwardedHeadersOptions.RealIp"/> is set: the
    /// platform middleware cannot process X-Real-IP, so honoring it silently would
    /// leave the header unprocessed.
    /// </summary>
    public static IServiceCollection AddPubSoftForwardedHeaders(
        this IServiceCollection services,
        Action<PubSoftForwardedHeadersOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new PubSoftForwardedHeadersOptions();
        configure?.Invoke(options);
        if (options.RealIp)
        {
            throw new ArgumentException(
                "X-Real-IP processing requires UsePubSoftForwardedHeaders — ForwardedHeadersOptions cannot express it.",
                nameof(configure));
        }
        _ = Apply(options); // validate eagerly: malformed trust entries fail at registration, not first resolve
        services.AddSingleton(Options.Create(options));
        services.Configure<ForwardedHeadersOptions>(fho => Apply(options, fho));
        return services;
    }

    private static ForwardedHeadersOptions Apply(PubSoftForwardedHeadersOptions options)
    {
        var fho = new ForwardedHeadersOptions();
        Apply(options, fho);
        return fho;
    }

    private static void Apply(PubSoftForwardedHeadersOptions options, ForwardedHeadersOptions fho)
    {
        var headers = ForwardedHeaders.None;
        if (options.ForwardedFor)
            headers |= ForwardedHeaders.XForwardedFor;
        if (options.ForwardedProto)
            headers |= ForwardedHeaders.XForwardedProto;
        fho.ForwardedHeaders = headers;
        fho.ForwardLimit = options.ForwardLimit;

        if (options.TrustAnyProxy)
        {
            fho.KnownProxies.Clear();
            fho.KnownIPNetworks.Clear();
            return;
        }

        foreach (var proxy in options.KnownProxies)
        {
            if (!IPAddress.TryParse(proxy, out var address))
                throw new ArgumentException($"KnownProxies entry \"{proxy}\" is not a valid IP address.", nameof(options));
            fho.KnownProxies.Add(address);
        }
        foreach (var network in options.KnownNetworks)
        {
            var parts = network.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2
                || !IPAddress.TryParse(parts[0], out var prefix)
                || !int.TryParse(parts[1], out var prefixLength))
                throw new ArgumentException($"KnownNetworks entry \"{network}\" is not valid CIDR notation (\"<address>/<prefix-length>\").", nameof(options));
            fho.KnownIPNetworks.Add(new System.Net.IPNetwork(prefix, prefixLength));
        }
    }

    private static void ApplyRealIpHeader(HttpContext context, ForwardedHeadersOptions forwardedHeaders, bool trustAnyProxy)
    {
        // X-Forwarded-For always wins: when the header was present, the platform
        // middleware already reached a verdict on RemoteIpAddress.
        if (context.Items[SocketPeerKey] is not SocketPeerState { HadForwardedFor: false } state)
            return;

        var header = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(header) || !IPAddress.TryParse(header.Trim(), out var realIp))
            return;

        if (!IsTrustedPeer(state.Peer, forwardedHeaders, trustAnyProxy))
            return;

        context.Connection.RemoteIpAddress = realIp;
    }

    private static bool IsTrustedPeer(IPAddress? peer, ForwardedHeadersOptions forwardedHeaders, bool trustAnyProxy)
    {
        if (peer is null)
            return false;
        if (trustAnyProxy)
            return true;

        var mapped = peer.IsIPv4MappedToIPv6 ? peer.MapToIPv4() : peer;
        foreach (var proxy in forwardedHeaders.KnownProxies)
        {
            if (proxy.Equals(peer) || proxy.Equals(mapped))
                return true;
        }
        foreach (var network in forwardedHeaders.KnownIPNetworks)
        {
            if (network.Contains(peer) || network.Contains(mapped))
                return true;
        }
        return false;
    }

    private sealed record SocketPeerState(IPAddress? Peer, bool HadForwardedFor);
}
