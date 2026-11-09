using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PubSoft.Domain;

namespace PubSoft.EntityFrameworkCore;

/// <summary>Base DbContext wiring tenant/member/soft-delete scope filters and the save-participant chain.</summary>
public abstract class PubSoftDbContext : DbContext, IPubSoftSaveParticipant
{
    /// <summary>Tenant query-filter key.</summary>
    public const string TenantFilter = "PubSoft.Tenant";
    /// <summary>Member query-filter key.</summary>
    public const string MemberFilter = "PubSoft.Member";
    /// <summary>Soft-delete query-filter key.</summary>
    public const string SoftDeleteFilter = "PubSoft.SoftDelete";

    private readonly ITenantContext _tenantContext;
    private readonly IMemberScopeContext? _memberScopeContext;
    private readonly TimeProvider _timeProvider = TimeProvider.System;
    private readonly ISaveChangesHook? _saveChangesHook;

    /// <summary>Creates the context with scope sources.</summary>
    protected PubSoftDbContext(
        DbContextOptions options,
        ITenantContext tenantContext,
        IMemberScopeContext? memberScopeContext = null,
        TimeProvider? timeProvider = null,
        ISaveChangesHook? saveChangesHook = null)
        : base(options)
    {
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _memberScopeContext = memberScopeContext;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _saveChangesHook = saveChangesHook;
    }

    /// <summary>Ambient tenant id.</summary>
    public long? CurrentTenantId => _tenantContext.TenantId;
    /// <summary>Ambient member id.</summary>
    public long? CurrentMemberId => _memberScopeContext?.MemberId;
    /// <summary>Whether member scoping is active.</summary>
    public bool IsMemberScoped => _memberScopeContext?.IsMemberScoped == true;

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        PrepareChanges();
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            var builder = modelBuilder.Entity(clrType);

            if (typeof(IMultiTenant).IsAssignableFrom(clrType))
            {
                builder.Property<long?>(nameof(IMultiTenant.TenantId)).IsConcurrencyToken();
                var parameter = Expression.Parameter(clrType, "entity");
                var tenantId = EfProperty<long?>(parameter, nameof(IMultiTenant.TenantId));
                var currentTenantId = Expression.Property(Expression.Constant(this), nameof(CurrentTenantId));
                builder.HasQueryFilter(TenantFilter, Expression.Lambda(Expression.Equal(tenantId, currentTenantId), parameter));
            }

            if (typeof(IMemberScoped).IsAssignableFrom(clrType))
            {
                builder.Property<long?>(nameof(IMemberScoped.MemberId)).IsConcurrencyToken();
                var parameter = Expression.Parameter(clrType, "entity");
                var memberId = EfProperty<long?>(parameter, nameof(IMemberScoped.MemberId));
                var currentMemberId = Expression.Property(Expression.Constant(this), nameof(CurrentMemberId));
                var isMemberScoped = Expression.Property(Expression.Constant(this), nameof(IsMemberScoped));
                var memberIdHasValue = Expression.Property(currentMemberId, nameof(Nullable<long>.HasValue));
                var memberMatches = Expression.Equal(memberId, currentMemberId);
                var filter = Expression.OrElse(
                    Expression.Not(isMemberScoped),
                    Expression.AndAlso(memberIdHasValue, memberMatches));
                builder.HasQueryFilter(MemberFilter, Expression.Lambda(filter, parameter));
            }

            if (typeof(ISoftDelete).IsAssignableFrom(clrType))
            {
                var parameter = Expression.Parameter(clrType, "entity");
                var isDeleted = EfProperty<bool>(parameter, nameof(ISoftDelete.IsDeleted));
                builder.HasQueryFilter(
                    SoftDeleteFilter,
                    Expression.Lambda(Expression.Equal(isDeleted, Expression.Constant(false)), parameter));
            }
        }
    }

    private void PrepareChanges()
    {
        foreach (var entry in ChangeTracker.Entries<IMultiTenant>())
        {
            if (entry.State == EntityState.Added && entry.Entity.TenantId is null)
                entry.Entity.TenantId = CurrentTenantId;
            else if (entry.State is EntityState.Added or EntityState.Modified
                     && CurrentTenantId is { } scoped
                     && entry.Entity.TenantId != scoped)
                throw new InvalidOperationException(
                    $"Cannot write {entry.Entity.GetType().Name} with TenantId {entry.Entity.TenantId} " +
                    $"outside the current tenant scope {scoped}.");
            else if (entry.State == EntityState.Deleted
                     && CurrentTenantId is { } delScoped
                     && entry.Entity.TenantId is { } delTenant
                     && delTenant != delScoped)
                throw new InvalidOperationException(
                    $"Cannot delete {entry.Entity.GetType().Name} with TenantId {delTenant} " +
                    $"outside the current tenant scope {delScoped}.");
        }

        foreach (var entry in ChangeTracker.Entries<IMemberScoped>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified
                && IsMemberScoped
                && entry.Entity.MemberId != CurrentMemberId)
                throw new InvalidOperationException(
                    $"Cannot write {entry.Entity.GetType().Name} with MemberId {entry.Entity.MemberId?.ToString() ?? "null"} " +
                    "outside the current member scope.");
            if (entry.State == EntityState.Deleted
                && IsMemberScoped
                && entry.Entity.MemberId is { } delMember
                && delMember != CurrentMemberId)
                throw new InvalidOperationException(
                    $"Cannot delete {entry.Entity.GetType().Name} with MemberId {delMember} " +
                    "outside the current member scope.");
        }

        _saveChangesHook?.BeforeSaveChanges(this);

        var deletedAt = _timeProvider.GetUtcNow().UtcDateTime;
        foreach (var entry in ChangeTracker.Entries<ISoftDelete>())
        {
            if (entry.State != EntityState.Deleted)
                continue;

            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
            entry.Entity.DeletionTime = deletedAt;
        }
    }

    private static Expression EfProperty<TProperty>(ParameterExpression parameter, string propertyName)
    {
        var method = typeof(EF).GetMethod(nameof(EF.Property))!.MakeGenericMethod(typeof(TProperty));
        return Expression.Call(method, parameter, Expression.Constant(propertyName));
    }
}