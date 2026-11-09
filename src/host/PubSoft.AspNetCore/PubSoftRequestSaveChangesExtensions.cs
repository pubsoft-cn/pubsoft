using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Domain;

namespace PubSoft.AspNetCore;


/// <summary>Registration for per-request SaveChanges.</summary>
public static class PubSoftRequestSaveChangesExtensions
{
    /// <summary>Registers the SaveChanges boundary.</summary>
    public static IServiceCollection AddPubSoftRequestSaveChanges(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<PubSoftRequestSaveChanges>();
        services.AddScoped<ISaveChangesBoundary>(provider => provider.GetRequiredService<PubSoftRequestSaveChanges>());
        return services;
    }

    /// <summary>Adds the per-request SaveChanges middleware.</summary>
    public static IApplicationBuilder UsePubSoftRequestSaveChanges(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<PubSoftRequestSaveChangesMiddleware>();
    }
}
