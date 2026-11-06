using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Domain;

namespace PubSoft.AspNetCore;


public static class PubSoftRequestSaveChangesExtensions
{
    public static IServiceCollection AddPubSoftRequestSaveChanges(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<PubSoftRequestSaveChanges>();
        services.AddScoped<ISaveChangesBoundary>(provider => provider.GetRequiredService<PubSoftRequestSaveChanges>());
        return services;
    }

    public static IApplicationBuilder UsePubSoftRequestSaveChanges(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<PubSoftRequestSaveChangesMiddleware>();
    }
}
