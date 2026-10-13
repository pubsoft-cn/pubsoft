using Microsoft.Extensions.DependencyInjection;
using PubSoft.AspNetCore.Authentication;

namespace PubSoft.Abp.AspNetCore;

public static class PubSoftAbpAspNetCoreServiceCollectionExtensions
{
    public static IServiceCollection AddPubSoftAbpAspNetCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.AddPrincipalSnapshotAccessor();
    }
}