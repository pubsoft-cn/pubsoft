namespace PubSoft.AspNetCore;

/// <summary>Rate-limit buckets for the gateway path classes (login, OAuth, validate, health, failure-rate).</summary>
public sealed class GatewayRateLimitOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Gateway:RateLimit";
    /// <summary>Whether rate limiting is enabled.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Default per-partition permit limit per window.</summary>
    public int PermitLimit { get; set; } = 200;
    /// <summary>Default window length in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;
    /// <summary>Queued requests allowed past the limit (0 = reject immediately).</summary>
    public int QueueLimit { get; set; }
    /// <summary>Whether partitioning is per endpoint.</summary>
    public bool PerEndpoint { get; set; } = true;
    /// <summary>Permit limit for login paths.</summary>
    public int LoginPermitLimit { get; set; } = 20;
    /// <summary>Login window length in seconds.</summary>
    public int LoginWindowSeconds { get; set; } = 60;
    /// <summary>Comma-separated path prefixes classified as login.</summary>
    public string LoginPathPrefixes { get; set; } = "/api/auth/admin-login,/api/auth/refresh-token,/api/auth/revoke";
    /// <summary>Permit limit for OAuth relay paths.</summary>
    public int OAuthPermitLimit { get; set; } = 2000;
    /// <summary>OAuth window length in seconds.</summary>
    public int OAuthWindowSeconds { get; set; } = 60;
    /// <summary>OAuth failure-strike limit before blocking.</summary>
    public int OAuthFailureLimit { get; set; } = 30;
    /// <summary>Comma-separated path prefixes classified as OAuth relay.</summary>
    public string OAuthPathPrefixes { get; set; } = "/api/auth/oauth/relay/exchange,/api/auth/oauth/relay/exchange-admin,/api/auth/oauth/relay/mini-program";
    /// <summary>Permit limit for token-validate paths.</summary>
    public int ValidatePermitLimit { get; set; } = 2000;
    /// <summary>Permit limit for anonymous validate requests.</summary>
    public int ValidateAnonymousPermitLimit { get; set; } = 10;
    /// <summary>Aggregate per-IP ceiling for validate routes -- applies to every request,
    /// with or without a client identity, so forged client IDs cannot multiply the total.</summary>
    public int ValidatePerIpPermitLimit { get; set; } = 1200;
    /// <summary>Validate window length in seconds.</summary>
    public int ValidateWindowSeconds { get; set; } = 60;
    /// <summary>Validate failure-strike limit before blocking.</summary>
    public int ValidateFailureLimit { get; set; } = 50;
    /// <summary>Comma-separated path prefixes classified as validate.</summary>
    public string ValidatePathPrefixes { get; set; } = "/api/auth/validate";
    /// <summary>Aggregate per-IP ceiling for OAuth routes -- same anti-partitioning role as
    /// <see cref="ValidatePerIpPermitLimit"/>.</summary>
    public int OAuthPerIpPermitLimit { get; set; } = 600;
    /// <summary>Permit limit for health-check paths.</summary>
    public int HealthPermitLimit { get; set; } = 60;
    /// <summary>Health window length in seconds.</summary>
    public int HealthWindowSeconds { get; set; } = 60;
    /// <summary>Comma-separated path prefixes classified as health.</summary>
    public string HealthPathPrefixes { get; set; } = "/health";
    /// <summary>Whether failure-strike limiting is enabled.</summary>
    public bool FailureRateLimitEnabled { get; set; } = true;
    /// <summary>Failure-strike permit limit.</summary>
    public int FailurePermitLimit { get; set; } = 50;
    /// <summary>Failure-strike window length in seconds.</summary>
    public int FailureWindowSeconds { get; set; } = 60;
    /// <summary>Whether 404-strike limiting is enabled.</summary>
    public bool NotFoundRateLimitEnabled { get; set; } = true;
    /// <summary>404-strike permit limit.</summary>
    public int NotFoundPermitLimit { get; set; } = 50;
    /// <summary>404-strike window length in seconds.</summary>
    public int NotFoundWindowSeconds { get; set; } = 60;
    /// <summary>Max distinct client identities one IP may claim inside
    /// <see cref="ClientIdWindowSeconds"/>. Beyond the cap the request is bucketed by IP
    /// alone -- rotating X-Client-Id cannot keep minting fresh limiter partitions.</summary>
    public int MaxClientIdsPerIp { get; set; } = 32;
    /// <summary>Client-id cardinality window in seconds.</summary>
    public int ClientIdWindowSeconds { get; set; } = 300;
    /// <summary>Upper bound on IPs tracked for client-ID cardinality. At capacity, unseen
    /// IPs fall back to IP-only bucketing.</summary>
    public int MaxTrackedIps { get; set; } = 10_000;
    /// <summary>Trusted reverse-proxy socket peers (IPv4/IPv6). Combined with
    /// <see cref="TrustedNetworks"/>: when either is non-empty, only listed peers may
    /// supply forwarded headers. When both are empty every peer is trusted -- safe only
    /// when the backend socket is reachable exclusively through the edge gateway.
    /// Invalid entries fail startup.</summary>
    public string[] TrustedProxies { get; set; } = [];
    /// <summary>Trusted proxy networks in CIDR notation ("172.16.0.0/12"). Invalid entries fail startup.</summary>
    public string[] TrustedNetworks { get; set; } = [];
    /// <summary>Max X-Forwarded-For entries consumed from the right. Null = process all entries.</summary>
    public int? ForwardLimit { get; set; }
}
