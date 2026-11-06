using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PubSoft.Edge;

/// <summary>
/// Edge rate-limit options -- partitioned by clientId + scheme + method.
///
/// Three layers:
/// 1. Total limit -- all requests counted, guards overall load
/// 2. Per-clientId limit -- guards a single third-party client
/// 3. Failure limit -- only failed requests counted (auth failures/handler errors), blocks signature probing
///
/// The path is not hard-coded; it follows the UseEdgeGateway path.
/// </summary>
public sealed class EdgeRateLimitOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "Edge:RateLimit";

    /// <summary>Whether rate limiting is enabled.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Fallback per-clientId total limit (requests per window).</summary>
    public int ClientPermitLimit { get; set; } = 1000;

    /// <summary>Total-limit window (seconds).</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Total-limit queue length (0 = reject immediately when exceeded).</summary>
    public int QueueLimit { get; set; } = 0;

    // ── per-scheme quotas ──

    /// <summary>Hmac-scheme limit (B2B, per clientId).</summary>
    public int HmacPermitLimit { get; set; } = 500;

    /// <summary>Session-scheme limit (C-end, per sessionKey).</summary>
    public int SessionPermitLimit { get; set; } = 2000;

    /// <summary>None-scheme limit (anonymous/login endpoints, per IP -- strict anti-brute-force).</summary>
    public int NonePermitLimit { get; set; } = 20;

    /// <summary>Md5-scheme limit (legacy clients, per clientId).</summary>
    public int Md5PermitLimit { get; set; } = 500;

    // ── failure limiting (blocks signature probing / brute force) ──

    /// <summary>Whether failure limiting is enabled.</summary>
    public bool FailureRateLimitEnabled { get; set; } = true;

    /// <summary>Failure limit (per clientId + IP).</summary>
    public int FailurePermitLimit { get; set; } = 30;

    /// <summary>Failure-limit window (seconds).</summary>
    public int FailureWindowSeconds { get; set; } = 60;

    /// <summary>None-scheme failure limit (login-endpoint anti-brute-force, per IP).</summary>
    public int NoneFailurePermitLimit { get; set; } = 10;
}
