// README anchor: src/data/PubSoft.EntityFrameworkCore/README.md — host DbContext shape
using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;

namespace PubSoft.DocsSnippets;

/// <summary>Host-style DbContext per the EF README example.</summary>
public sealed class DocDbContext(
    DbContextOptions<DocDbContext> options,
    ITenantContext tenant,
    IMemberScopeContext memberScope)
    : PubSoftDbContext(options, tenant, memberScope)
{
    /// <summary>Example aggregate set.</summary>
    public DbSet<DocOrder> Orders => Set<DocOrder>();
}

/// <summary>Example aggregate root.</summary>
public sealed class DocOrder : PubSoft.Domain.AggregateRoot<long> { }
