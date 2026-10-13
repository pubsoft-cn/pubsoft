using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;

namespace PubSoft.EntityFrameworkCore;

public sealed class EfCoreRepository<TEntity, TKey, TDbContext> : IEfCoreRepository<TEntity, TKey>, IReadOnlyRepository<TEntity, TKey>
    where TEntity : class
    where TDbContext : PubSoftDbContext
{
    private readonly TDbContext _dbContext;
    private readonly DbSet<TEntity> _dbSet;

    public EfCoreRepository(TDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _dbSet = dbContext.Set<TEntity>();
    }

    public Task<IQueryable<TEntity>> GetQueryableAsync()
        => Task.FromResult<IQueryable<TEntity>>(_dbSet);

    public Task<DbSet<TEntity>> GetDbSetAsync()
        => Task.FromResult(_dbSet);

    public async Task<TEntity> GetAsync(TKey id, CancellationToken cancellationToken = default)
        => await FindAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"{typeof(TEntity).Name} with id '{id}' not found.");

    public async Task<TEntity?> FindAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var keyProperty = GetSingleKeyProperty();
        var trackedEntry = _dbContext.ChangeTracker.Entries<TEntity>()
            .FirstOrDefault(entry => Equals(entry.Property(keyProperty.Name).CurrentValue, id));

        if (trackedEntry is not null)
            return IsVisible(trackedEntry.Entity, trackedEntry.State) ? trackedEntry.Entity : null;

        var predicate = CreateKeyPredicate(keyProperty.Name, id);
        var entity = await _dbSet.Where(predicate).FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (entity is null)
            return null;

        var state = _dbContext.Entry(entity).State;
        return IsVisible(entity, state) ? entity : null;
    }

    public async Task<TEntity> InsertAsync(TEntity entity, bool autoSave = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        EnsureCurrentScope(entity, allowUnassignedTenant: true);
        await _dbSet.AddAsync(entity, cancellationToken).ConfigureAwait(false);
        if (autoSave)
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return entity;
    }

    public async Task<TEntity> UpdateAsync(TEntity entity, bool autoSave = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        EnsureCurrentScope(entity);
        var id = GetEntityKey(entity);
        var existing = await FindAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"{typeof(TEntity).Name} with id '{id}' not found in the current scope.");
        ApplyUpdate(existing, entity);
        if (autoSave)
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return existing;
    }

    public async Task DeleteAsync(TKey id, bool autoSave = false, CancellationToken cancellationToken = default)
    {
        var entity = await FindAsync(id, cancellationToken).ConfigureAwait(false);
        if (entity is null)
            return;

        _dbSet.Remove(entity);
        if (autoSave)
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(TEntity entity, bool autoSave = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        EnsureCurrentScope(entity);
        var existing = await FindAsync(GetEntityKey(entity), cancellationToken).ConfigureAwait(false);
        if (existing is null)
            return;

        _dbSet.Remove(existing);
        if (autoSave)
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateManyAsync(IEnumerable<TEntity> entities, bool autoSave = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var entityArray = entities.ToArray();
        foreach (var entity in entityArray)
        {
            ArgumentNullException.ThrowIfNull(entity);
            EnsureCurrentScope(entity);
        }

        var keyProperty = GetSingleKeyProperty();
        var updates = new List<(TEntity Existing, TEntity Supplied)>();
        var ids = new HashSet<TKey>();
        foreach (var entity in entityArray)
        {
            ArgumentNullException.ThrowIfNull(entity);
            var id = GetEntityKey(entity, keyProperty);
            if (!ids.Add(id))
                throw new ArgumentException("The update collection contains duplicate entity keys.", nameof(entities));

            var existing = await FindAsync(id, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"{typeof(TEntity).Name} with id '{id}' not found in the current scope.");
            updates.Add((existing, entity));
        }

        foreach (var (existing, supplied) in updates)
            ApplyUpdate(existing, supplied);

        if (autoSave)
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task InsertManyAsync(IEnumerable<TEntity> entities, bool autoSave = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entities);
        var entityArray = entities.ToArray();
        foreach (var entity in entityArray)
        {
            ArgumentNullException.ThrowIfNull(entity);
            EnsureCurrentScope(entity, allowUnassignedTenant: true);
        }

        await _dbSet.AddRangeAsync(entityArray, cancellationToken).ConfigureAwait(false);
        if (autoSave)
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

    private Microsoft.EntityFrameworkCore.Metadata.IProperty GetSingleKeyProperty()
    {
        var entityType = _dbContext.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"Entity type '{typeof(TEntity).Name}' is not part of the DbContext model.");
        var primaryKey = entityType.FindPrimaryKey()
            ?? throw new InvalidOperationException($"Entity type '{typeof(TEntity).Name}' does not have a primary key.");

        if (primaryKey.Properties.Count != 1)
            throw new NotSupportedException($"Repository key lookup supports only single-column primary keys; '{typeof(TEntity).Name}' has {primaryKey.Properties.Count} columns.");

        var keyProperty = primaryKey.Properties[0];
        if (keyProperty.ClrType != typeof(TKey))
            throw new InvalidOperationException($"Repository key type '{typeof(TKey).Name}' does not match entity key type '{keyProperty.ClrType.Name}'.");

        return keyProperty;
    }

    private TKey GetEntityKey(TEntity entity)
        => GetEntityKey(entity, GetSingleKeyProperty());

    private TKey GetEntityKey(TEntity entity, Microsoft.EntityFrameworkCore.Metadata.IProperty keyProperty)
    {
        var key = _dbContext.Entry(entity).Property(keyProperty.Name).CurrentValue;
        return key is TKey id
            ? id
            : throw new InvalidOperationException($"Entity '{typeof(TEntity).Name}' has no valid '{keyProperty.Name}' key.");
    }

    private void ApplyUpdate(TEntity existing, TEntity supplied)
    {
        if (ReferenceEquals(existing, supplied))
        {
            _dbSet.Update(existing);
            return;
        }

        _dbContext.Entry(existing).CurrentValues.SetValues(supplied);
    }

    private static Expression<Func<TEntity, bool>> CreateKeyPredicate(string keyName, TKey id)
    {
        ArgumentNullException.ThrowIfNull(id);
        var parameter = Expression.Parameter(typeof(TEntity), "entity");
        var efProperty = typeof(EF).GetMethod(nameof(EF.Property))!.MakeGenericMethod(typeof(TKey));
        var key = Expression.Call(efProperty, parameter, Expression.Constant(keyName));
        var equals = Expression.Equal(key, Expression.Constant(id, typeof(TKey)));
        return Expression.Lambda<Func<TEntity, bool>>(equals, parameter);
    }

    private bool IsVisible(TEntity entity, EntityState state)
    {
        if (state is EntityState.Detached or EntityState.Deleted)
            return false;
        if (entity is IMultiTenant tenantEntity && tenantEntity.TenantId != _dbContext.CurrentTenantId)
            return false;
        if (entity is IMemberScoped memberEntity && _dbContext.IsMemberScoped)
        {
            if (_dbContext.CurrentMemberId is not long memberId || memberEntity.MemberId != memberId)
                return false;
        }
        if (entity is ISoftDelete softDeleteEntity && softDeleteEntity.IsDeleted)
            return false;
        return true;
    }

    private void EnsureCurrentScope(TEntity entity, bool allowUnassignedTenant = false)
    {
        if (entity is IMultiTenant tenantEntity
            && tenantEntity.TenantId != _dbContext.CurrentTenantId
            && !(allowUnassignedTenant && tenantEntity.TenantId is null))
            throw new InvalidOperationException("The entity is outside the current tenant scope.");

        if (entity is IMemberScoped memberEntity && _dbContext.IsMemberScoped)
        {
            if (_dbContext.CurrentMemberId is not long memberId || memberEntity.MemberId != memberId)
                throw new InvalidOperationException("The entity is outside the current member scope.");
        }
    }
}