using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PubSoft.Security;

namespace PubSoft.AspNetCore;


public sealed class HmacValidationOptions
{
    /// <summary>豁免路径前缀——按路径段边界匹配（"/health" 只豁免 "/health" 与 "/health/..."，不放行 "/healthz-x"）。</summary>
    public string[] ExcludedPathPrefixes { get; set; } = [];
    /// <summary>请求体 SHA-256 是否入签——默认开启；GET/空 body 用固定空体哈希，不豁免。</summary>
    public bool RequireBodyHash { get; set; } = true;
    public int MaximumBodyBytes { get; set; } = 1_048_576;
}
