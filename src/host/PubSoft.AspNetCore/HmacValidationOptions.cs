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
    /// <summary>Path prefixes exempt from validation -- segment-boundary matched ("/health" exempts "/health" and "/health/..." but not "/healthz-x").</summary>
    public string[] ExcludedPathPrefixes { get; set; } = [];
    /// <summary>Whether the request body SHA-256 is signed -- on by default; GET/empty bodies use a fixed empty-body hash, no exemption.</summary>
    public bool RequireBodyHash { get; set; } = true;
    public int MaximumBodyBytes { get; set; } = 1_048_576;

    /// <summary>Signature-related header names -- overridable for compatibility with existing client protocols.</summary>
    public string AppIdHeader { get; set; } = HmacSignatureHeaders.AppId;
    public string SignatureHeader { get; set; } = HmacSignatureHeaders.Signature;
    public string TimestampHeader { get; set; } = HmacSignatureHeaders.Timestamp;
    public string NonceHeader { get; set; } = HmacSignatureHeaders.Nonce;
}
