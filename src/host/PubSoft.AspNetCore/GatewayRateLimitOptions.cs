
using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace PubSoft.AspNetCore;

public sealed class GatewayRateLimitOptions
{
    public const string SectionName = "Gateway:RateLimit";
    public bool Enabled { get; set; } = true;
    public int PermitLimit { get; set; } = 200;
    public int WindowSeconds { get; set; } = 60;
    public int QueueLimit { get; set; }
    public bool PerEndpoint { get; set; } = true;
    public int LoginPermitLimit { get; set; } = 20;
    public int LoginWindowSeconds { get; set; } = 60;
    public string LoginPathPrefixes { get; set; } = "/api/auth/admin-login,/api/auth/refresh-token,/api/auth/revoke";
    public int OAuthPermitLimit { get; set; } = 2000;
    public int OAuthWindowSeconds { get; set; } = 60;
    public int OAuthFailureLimit { get; set; } = 30;
    public string OAuthPathPrefixes { get; set; } = "/api/auth/oauth/relay/exchange,/api/auth/oauth/relay/exchange-admin,/api/auth/oauth/relay/mini-program";
    public int ValidatePermitLimit { get; set; } = 2000;
    public int ValidateAnonymousPermitLimit { get; set; } = 10;
    /// <summary>Aggregate per-IP ceiling for validate routes — applies to every request,
    /// with or without a client identity, so forged client IDs cannot multiply the total.</summary>
    public int ValidatePerIpPermitLimit { get; set; } = 1200;
    public int ValidateWindowSeconds { get; set; } = 60;
    public int ValidateFailureLimit { get; set; } = 50;
    public string ValidatePathPrefixes { get; set; } = "/api/auth/validate";
    /// <summary>Aggregate per-IP ceiling for OAuth routes — same anti-partitioning role as
    /// <see cref="ValidatePerIpPermitLimit"/>.</summary>
    public int OAuthPerIpPermitLimit { get; set; } = 600;
    public int HealthPermitLimit { get; set; } = 60;
    public int HealthWindowSeconds { get; set; } = 60;
    public string HealthPathPrefixes { get; set; } = "/health";
    public bool FailureRateLimitEnabled { get; set; } = true;
    public int FailurePermitLimit { get; set; } = 50;
    public int FailureWindowSeconds { get; set; } = 60;
    public bool NotFoundRateLimitEnabled { get; set; } = true;
    public int NotFoundPermitLimit { get; set; } = 50;
    public int NotFoundWindowSeconds { get; set; } = 60;
    /// <summary>Max distinct client identities one IP may claim inside
    /// <see cref="ClientIdWindowSeconds"/>. Beyond the cap the request is bucketed by IP
    /// alone — rotating X-Client-Id cannot keep minting fresh limiter partitions.</summary>
    public int MaxClientIdsPerIp { get; set; } = 32;
    public int ClientIdWindowSeconds { get; set; } = 300;
    /// <summary>Upper bound on IPs tracked for client-ID cardinality. At capacity, unseen
    /// IPs fall back to IP-only bucketing.</summary>
    public int MaxTrackedIps { get; set; } = 10_000;
    /// <summary>Trusted reverse-proxy socket peers (IPv4/IPv6). Combined with
    /// <see cref="TrustedNetworks"/>: when either is non-empty, only listed peers may
    /// supply forwarded headers. When both are empty every peer is trusted — safe only
    /// when the backend socket is reachable exclusively through the edge gateway.
    /// Invalid entries fail startup.</summary>
    public string[] TrustedProxies { get; set; } = [];
    /// <summary>Trusted proxy networks in CIDR notation ("172.16.0.0/12"). Invalid entries fail startup.</summary>
    public string[] TrustedNetworks { get; set; } = [];
    /// <summary>Max X-Forwarded-For entries consumed from the right. Null = process all entries.</summary>
    public int? ForwardLimit { get; set; }
}
