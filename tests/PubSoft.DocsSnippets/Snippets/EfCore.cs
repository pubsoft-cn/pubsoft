// README anchor: src/data/PubSoft.EntityFrameworkCore/README.md — audit persistence + repos
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.DependencyInjection;
using PubSoft.EntityFrameworkCore;
using PubSoft.EntityFrameworkCore.Auditing;

namespace PubSoft.DocsSnippets;

/// <summary>Snippets: EF registration + persistence wiring.</summary>
internal static class EfCoreSnippets
{
    // "AddPubSoftEfCoreAuditPersistence" + AddPubSoftRepository
    internal static void Register(IServiceCollection services, long workerId)
    {
        services.AddPubSoftIdGenerator(workerId);
        services.AddPubSoftEfCoreAuditPersistence<DocDbContext>();
        services.AddPubSoftRepository<DocOrder, long, DocDbContext>();
    }

    // modelBuilder calls from README
    internal static void Model(ModelBuilder b)
    {
        b.ConfigurePubSoftAuditPersistence();
        b.ConfigurePubSoftIdentityUsers();
        b.ConfigurePubSoftIdentityUserLogins();
        b.ConfigurePubSoftLegacyIdMaps();
        b.ApplySnowflakeIdConventions();
    }
}
