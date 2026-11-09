using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;

namespace PubSoft.EntityFrameworkCore;

/// <summary>Unit of work wrapping a typed <see cref="DbContext"/>.</summary>
public interface IPubSoftUnitOfWork<out TDbContext> where TDbContext : DbContext
{
    /// <summary>The wrapped context.</summary>
    TDbContext DbContext { get; }
    /// <summary>Commits the ambient unit of work.</summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Default unit of work.</summary>
public sealed class PubSoftUnitOfWork<TDbContext> : IPubSoftUnitOfWork<TDbContext>, IPubSoftSaveParticipant
    where TDbContext : DbContext
{
    /// <summary>Creates the unit of work.</summary>
    public PubSoftUnitOfWork(TDbContext dbContext)
        => DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    /// <summary>The wrapped context.</summary>
    public TDbContext DbContext { get; }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => DbContext.SaveChangesAsync(cancellationToken);
}
