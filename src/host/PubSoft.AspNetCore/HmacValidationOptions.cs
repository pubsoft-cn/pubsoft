using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PubSoft.Security;

namespace PubSoft.AspNetCore;


/// <summary>HMAC signature-validation options.</summary>
public sealed class HmacValidationOptions
{
    /// <summary>Path prefixes exempt from validation -- segment-boundary matched ("/health" exempts "/health" and "/health/..." but not "/healthz-x").</summary>
    public string[] ExcludedPathPrefixes { get; set; } = [];
    /// <summary>Whether the request body SHA-256 is signed -- on by default; GET/empty bodies use a fixed empty-body hash, no exemption.</summary>
    public bool RequireBodyHash { get; set; } = true;
    /// <summary>Body size bound for hashing.</summary>
    public int MaximumBodyBytes { get; set; } = 1_048_576;

    /// <summary>Signature-related header names -- overridable for compatibility with existing client protocols.</summary>
    public string AppIdHeader { get; set; } = HmacSignatureHeaders.AppId;
    /// <summary>Header carrying the signature.</summary>
    public string SignatureHeader { get; set; } = HmacSignatureHeaders.Signature;
    /// <summary>Header carrying the timestamp.</summary>
    public string TimestampHeader { get; set; } = HmacSignatureHeaders.Timestamp;
    /// <summary>Header carrying the replay nonce.</summary>
    public string NonceHeader { get; set; } = HmacSignatureHeaders.Nonce;
}
