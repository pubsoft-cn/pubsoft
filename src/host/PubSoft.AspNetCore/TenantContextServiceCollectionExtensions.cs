using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Domain;

namespace PubSoft.AspNetCore;


public static class TenantContextServiceCollectionExtensions
{
    public static IServiceCollection AddPubSoftTenantContext(
        this IServiceCollection services, Action<HttpContextTenantOptions>? configure = null)
    {
        var options = new HttpContextTenantOptions();
        configure?.Invoke(options);
        services.AddScoped<ITenantContext>(sp =>
            new HttpContextTenantContext(sp.GetRequiredService<IHttpContextAccessor>(), options));
        return services;
    }
}
