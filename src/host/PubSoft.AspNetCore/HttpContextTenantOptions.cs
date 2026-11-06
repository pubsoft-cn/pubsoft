using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Domain;

namespace PubSoft.AspNetCore;

public sealed class HttpContextTenantOptions
{
    /// <summary>是否接受请求头租户覆盖。默认 false——header 可被客户端伪造，仅可信网关剥离外部头后开启。</summary>
    public bool ReadHeader { get; set; }
    public string HeaderName { get; set; } = "X-Tenant-Id";
    public string[] ClaimNames { get; set; } = { "tid", "tenant_id" };
}
