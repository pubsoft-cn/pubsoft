using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;

namespace PubSoft.EntityFrameworkCore;

public interface IPubSoftUnitOfWork<out TDbContext> where TDbContext : DbContext
{
    TDbContext DbContext { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public sealed class PubSoftUnitOfWork<TDbContext> : IPubSoftUnitOfWork<TDbContext>, IPubSoftSaveParticipant
    where TDbContext : DbContext
{
    public PubSoftUnitOfWork(TDbContext dbContext)
        => DbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));

    public TDbContext DbContext { get; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => DbContext.SaveChangesAsync(cancellationToken);
}
