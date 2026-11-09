using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Auditing;
using PubSoft.Authentication;

namespace PubSoft.AspNetCore.Auditing;

/// <summary>Registration helpers for the audit-actor bridge.</summary>
public static class AuditActorServiceCollectionExtensions
{
    /// <summary>Wires <see cref="IAuditActorAccessor"/> to the resolved <see cref="CurrentSubject"/>.</summary>
    public static IServiceCollection AddCurrentSubjectAuditActor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IAuditActorAccessor, CurrentSubjectAuditActorAccessor>();
        return services;
    }
}
