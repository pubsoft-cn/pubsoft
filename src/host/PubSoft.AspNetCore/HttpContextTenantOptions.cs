using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Domain;

namespace PubSoft.AspNetCore;

public sealed class HttpContextTenantOptions
{
    /// <summary>Whether a tenant-id request header overrides the ambient tenant. Default false -- the header is client-forgeable; enable only behind a trusted gateway that strips external headers.</summary>
    public bool ReadHeader { get; set; }
    public string HeaderName { get; set; } = "X-Tenant-Id";
    public string[] ClaimNames { get; set; } = { "tid", "tenant_id" };
}
