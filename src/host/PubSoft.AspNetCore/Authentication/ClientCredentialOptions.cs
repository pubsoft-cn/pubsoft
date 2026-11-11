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


/// <summary>Configurable pieces of machine credential authorization.</summary>
public sealed class ClientCredentialOptions
{
    /// <summary>Header carrying the client id.</summary>
    public string ClientIdHeader { get; set; } = "X-Client-Id";
    /// <summary>Header carrying the client secret.</summary>
    public string ClientSecretHeader { get; set; } = "X-Client-Secret";
    /// <summary>Claim type for the client id.</summary>
    public string ClientIdClaimType { get; set; } = "client_id";
    /// <summary>Claim type for the caller type.</summary>
    public string CallerTypeClaimType { get; set; } = "caller_type";
    /// <summary>Caller-type value for machine callers.</summary>
    public string CallerTypeMachine { get; set; } = "Machine";

    /// <summary>Header carrying the client assertion -- presence switches to bearer-assertion mode (highest precedence).</summary>
    public string ClientAssertionHeader { get; set; } = "X-Client-Assertion";

    /// <summary>Header carrying the request signature -- presence switches to signed-credential mode.</summary>
    public string SignatureHeader { get; set; } = "X-Signature";
    /// <summary>Header carrying the signing timestamp.</summary>
    public string TimestampHeader { get; set; } = "X-Timestamp";
    /// <summary>Header carrying the per-request nonce.</summary>
    public string NonceHeader { get; set; } = "X-Nonce";
}
