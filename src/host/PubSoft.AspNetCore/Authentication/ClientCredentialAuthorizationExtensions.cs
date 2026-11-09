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


/// <summary>Registration helpers for client-credential authorization.</summary>
public static class ClientCredentialAuthorizationExtensions
{
    /// <summary>Registers the MVC action filter enforcing <see cref="RequireClientCredentialAttribute"/>.</summary>
    public static IMvcBuilder AddPubSoftClientCredentialAuthorization(this IMvcBuilder mvcBuilder)
    {
        ArgumentNullException.ThrowIfNull(mvcBuilder);
        mvcBuilder.Services.TryAddScoped<ClientCredentialMvcAuthorizationFilter>();
        return mvcBuilder.AddMvcOptions(options => options.Filters.AddService<ClientCredentialMvcAuthorizationFilter>());
    }

    /// <summary>Adds the machine-credential endpoint filter to a minimal-API route.</summary>
    public static RouteHandlerBuilder AddPubSoftClientCredential(this RouteHandlerBuilder endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        return endpoint.AddEndpointFilter<ClientCredentialEndpointFilter>();
    }

    /// <summary>Attaches the client-credential filter to the route group.</summary>
    public static RouteGroupBuilder AddPubSoftClientCredential(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);
        return group.AddEndpointFilter<ClientCredentialEndpointFilter>();
    }
}
