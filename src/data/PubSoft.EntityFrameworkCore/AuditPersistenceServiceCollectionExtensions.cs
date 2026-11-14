using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Auditing;

namespace PubSoft.EntityFrameworkCore.Auditing;


/// <summary>Registration for EF audit persistence.</summary>
public static class AuditPersistenceServiceCollectionExtensions
{
    /// <summary>Registers the EF audit sink on <typeparamref name="TDbContext"/>. Requires a registered <see cref="PubSoft.Core.IIdGenerator"/>.</summary>
    public static IServiceCollection AddPubSoftEfCoreAuditPersistence<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IAuditSnapshotSink, EfCoreAuditSnapshotSink<TDbContext>>();
        services.AddScoped<IAuditLogWriter, AuditLogWriter>();
        return services;
    }
}
