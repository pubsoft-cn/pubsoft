using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Auditing;
using PubSoft.Domain;

namespace PubSoft.EntityFrameworkCore.Auditing;


public static class AuditPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddPubSoftEfCoreAuditPersistence<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IAuditSnapshotSink, EfCoreAuditSnapshotSink<TDbContext>>();
        services.AddScoped<IAuditLogWriter, AuditLogWriter>();
        return services;
    }
}
