using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PubSoft.Security.EntityFrameworkCore;

/// <summary>
/// EF-backed <see cref="IRefreshTokenFamilyStore"/> for hosts that need rotation state to
/// survive restarts. The consume path is one atomic conditional UPDATE (single statement,
/// NOT EXISTS on the revocation table), so concurrent consumes across instances still resolve
/// to a single winner. The host DbContext must map the tables via
/// <see cref="SecurityStateModelBuilderExtensions.ConfigurePubSoftSecurityState"/>.
/// </summary>
public sealed class EfCoreRefreshTokenFamilyStore<TDbContext> : IRefreshTokenFamilyStore
    where TDbContext : DbContext
{
    private const int MaxFamilyIdLength = 64;
    private const int MaxSubjectLength = 256;
    private const int MaxHashLength = 64;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PubSoft.Core.IIdGenerator _idGenerator;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the store.</summary>
    /// <param name="scopeFactory">Scope factory for per-operation DbContext scopes.</param>
    /// <param name="idGenerator">Snowflake id source for new rows.</param>
    /// <param name="timeProvider">Clock; UTC real time when null.</param>
    public EfCoreRefreshTokenFamilyStore(IServiceScopeFactory scopeFactory, PubSoft.Core.IIdGenerator idGenerator, TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async ValueTask<RefreshTokenFamilyRecord?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        CheckHash(tokenHash);
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var state = await context.Set<RefreshTokenFamilyStateRecord>()
            .AsNoTracking()
            .FirstOrDefaultAsync(record => record.TokenHash == tokenHash, cancellationToken) // pubsoft:exempt PUBSOFT004 "EF Core cannot translate string.Equals with StringComparison - == is the translatable form"
            .ConfigureAwait(false);
        return state is null
            ? null
            : new RefreshTokenFamilyRecord(state.TokenHash, state.FamilyId, state.Subject, state.IssuedAt, state.TokenExpiresAt, state.FamilyExpiresAt, state.Consumed);
    }

    /// <inheritdoc />
    public async ValueTask SaveAsync(RefreshTokenFamilyRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        CheckHash(record.TokenHash);
        CheckBounded(record.FamilyId, MaxFamilyIdLength, nameof(record));
        CheckBounded(record.Subject, MaxSubjectLength, nameof(record));

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TDbContext>();
        context.Set<RefreshTokenFamilyStateRecord>().Add(new RefreshTokenFamilyStateRecord
        {
            Id = _idGenerator.NextId(),
            TokenHash = record.TokenHash,
            FamilyId = record.FamilyId,
            Subject = record.Subject,
            IssuedAt = record.IssuedAt,
            TokenExpiresAt = record.TokenExpiresAt,
            FamilyExpiresAt = record.FamilyExpiresAt,
            Consumed = record.Consumed
        });
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception)
        {
            throw new InvalidOperationException("Refresh token record violates a uniqueness constraint.", exception);
        }
    }

    /// <inheritdoc />
    public async ValueTask<RefreshConsumeResult> TryConsumeAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        CheckHash(tokenHash);
        var now = _timeProvider.GetUtcNow();
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var revocations = context.Set<RefreshTokenFamilyRevocationRecord>();
        var revokedFamilies = revocations
            .Where(revocation => revocation.ExpiresAt > now)
            .Select(revocation => revocation.FamilyId);

        var updated = await context.Set<RefreshTokenFamilyStateRecord>()
            .Where(record => record.TokenHash == tokenHash // pubsoft:exempt PUBSOFT004 "EF Core cannot translate string.Equals with StringComparison - == is the translatable form"
                && !record.Consumed
                && !revokedFamilies.Contains(record.FamilyId))
            .ExecuteUpdateAsync(setters => setters.SetProperty(record => record.Consumed, true), cancellationToken)
            .ConfigureAwait(false);
        if (updated == 1)
            return RefreshConsumeResult.Consumed;

        var state = await context.Set<RefreshTokenFamilyStateRecord>()
            .AsNoTracking()
            .FirstOrDefaultAsync(record => record.TokenHash == tokenHash, cancellationToken) // pubsoft:exempt PUBSOFT004 "EF Core cannot translate string.Equals with StringComparison - == is the translatable form"
            .ConfigureAwait(false);
        if (state is null)
            return RefreshConsumeResult.AlreadyConsumed;

        var revoked = await revocations
            .AnyAsync(revocation => revocation.FamilyId == state.FamilyId && revocation.ExpiresAt > now, cancellationToken) // pubsoft:exempt PUBSOFT004 "EF Core cannot translate string.Equals with StringComparison - == is the translatable form"
            .ConfigureAwait(false);
        return revoked ? RefreshConsumeResult.FamilyRevoked : RefreshConsumeResult.AlreadyConsumed;
    }

    /// <inheritdoc />
    public async ValueTask RevokeFamilyAsync(string familyId, DateTimeOffset until, CancellationToken cancellationToken = default)
    {
        CheckBounded(familyId, MaxFamilyIdLength, nameof(familyId));
        if (until <= _timeProvider.GetUtcNow())
            return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await UpsertRevocationAsync(context, familyId, until, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<int> RevokeSubjectAsync(string subject, DateTimeOffset until, CancellationToken cancellationToken = default)
    {
        CheckBounded(subject, MaxSubjectLength, nameof(subject));
        var now = _timeProvider.GetUtcNow();
        if (until <= now)
            return 0;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var familyIds = await context.Set<RefreshTokenFamilyStateRecord>()
            .Where(record => record.Subject == subject && record.FamilyExpiresAt > now) // pubsoft:exempt PUBSOFT004 "EF Core cannot translate string.Equals with StringComparison - == is the translatable form"
            .Select(record => record.FamilyId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var familyId in familyIds)
            await UpsertRevocationAsync(context, familyId, until, cancellationToken).ConfigureAwait(false);
        return familyIds.Count;
    }

    /// <summary>
    /// Deletes expired token records and revocation markers. Not part of the contract -
    /// hosts call this on their own schedule for retention hygiene.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask<int> PruneExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var markers = await context.Set<RefreshTokenFamilyRevocationRecord>()
            .Where(record => record.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        var tokens = await context.Set<RefreshTokenFamilyStateRecord>()
            .Where(record => record.TokenExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
        return markers + tokens;
    }

    private async Task UpsertRevocationAsync(TDbContext context, string familyId, DateTimeOffset until, CancellationToken cancellationToken)
    {
        var existing = await context.Set<RefreshTokenFamilyRevocationRecord>()
            .FirstOrDefaultAsync(record => record.FamilyId == familyId, cancellationToken) // pubsoft:exempt PUBSOFT004 "EF Core cannot translate string.Equals with StringComparison - == is the translatable form"
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.ExpiresAt < until)
                existing.ExpiresAt = until;
        }
        else
        {
            context.Set<RefreshTokenFamilyRevocationRecord>().Add(new RefreshTokenFamilyRevocationRecord
            {
                Id = _idGenerator.NextId(),
                FamilyId = familyId,
                ExpiresAt = until
            });
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Concurrent insert of the same family marker - fall back to the update path.
            context.ChangeTracker.Clear();
            var winner = await context.Set<RefreshTokenFamilyRevocationRecord>()
                .FirstOrDefaultAsync(record => record.FamilyId == familyId, cancellationToken) // pubsoft:exempt PUBSOFT004 "EF Core cannot translate string.Equals with StringComparison - == is the translatable form"
                .ConfigureAwait(false);
            if (winner is not null && winner.ExpiresAt < until)
            {
                winner.ExpiresAt = until;
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static void CheckHash(string tokenHash)
    {
        if (string.IsNullOrEmpty(tokenHash) || tokenHash.Length > MaxHashLength)
            throw new ArgumentException("TokenHash must be non-empty and <=64 characters.", nameof(tokenHash));
    }

    private static void CheckBounded(string value, int maxLength, string paramName)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maxLength)
            throw new ArgumentException($"Value must be non-empty and <={maxLength} characters.", paramName);
    }
}
