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
    public string ClientIdHeader { get; set; } = "X-Client-Id";
    public string ClientSecretHeader { get; set; } = "X-Client-Secret";
    public string ClientIdClaimType { get; set; } = "client_id";
    public string CallerTypeClaimType { get; set; } = "caller_type";
    public string CallerTypeMachine { get; set; } = "Machine";
}
