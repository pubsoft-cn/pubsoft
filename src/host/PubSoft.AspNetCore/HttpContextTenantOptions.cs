using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Domain;

namespace PubSoft.AspNetCore;

/// <summary>Tenant-resolution options for <see cref="HttpContextTenantContext"/>.</summary>
public sealed class HttpContextTenantOptions
{
    /// <summary>Whether a tenant-id request header overrides the ambient tenant. Default false -- the header is client-forgeable; enable only behind a trusted gateway that strips external headers.</summary>
    public bool ReadHeader { get; set; }
    /// <summary>Request header carrying the tenant id.</summary>
    public string HeaderName { get; set; } = "X-Tenant-Id";
    /// <summary>JWT claim names consulted for the tenant id.</summary>
    public string[] ClaimNames { get; set; } = { "tid", "tenant_id" };
}
