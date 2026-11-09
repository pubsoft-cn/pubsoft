using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Domain;

namespace PubSoft.AspNetCore.Authentication;


/// <summary>Outcome of a client-credential verification.</summary>
public sealed class ClientCredentialVerificationResult
{
    /// <summary>Whether verification passed.</summary>
    public bool Success { get; init; }

    /// <summary>Canonical client identifier to attach to the machine principal.</summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// Optional tenant to scope the ambient tenant context to for the remainder of the request
    /// (used when the credential resolves to a tenant-owned client).
    /// </summary>
    public long? TenantId { get; init; }

    /// <summary>Additional claims to attach to the machine principal.</summary>
    public IReadOnlyDictionary<string, string>? Claims { get; init; }

    /// <summary>Builds a failed result.</summary>
    public static ClientCredentialVerificationResult Fail() => new();
    /// <summary>Builds a successful result.</summary>
    public static ClientCredentialVerificationResult Ok(string clientId, long? tenantId = null,
        IReadOnlyDictionary<string, string>? claims = null)
        => new() { Success = true, ClientId = clientId, TenantId = tenantId, Claims = claims };
}
