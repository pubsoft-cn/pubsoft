using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace PubSoft.Security.EntityFrameworkCore;

/// <summary>
/// EF-backed <see cref="ITokenDenylist"/> for hosts that need revocation state to survive
/// restarts. Entries match only while unexpired; expired rows linger until
/// <see cref="PruneExpiredAsync"/> runs on the host's schedule. The host DbContext must map
/// the table via <see cref="SecurityStateModelBuilderExtensions.ConfigurePubSoftSecurityState"/>.
/// </summary>
public sealed class EfCoreTokenDenylist<TDbContext> : ITokenDenylist
    where TDbContext : DbContext
{
    private const int MaxTokenIdLength = 128;
    private const int MaxFieldLength = 256;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PubSoft.Core.IIdGenerator _idGenerator;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the denylist.</summary>
    /// <param name="scopeFactory">Scope factory for per-operation DbContext scopes.</param>
    /// <param name="idGenerator">Snowflake id source for new rows.</param>
    /// <param name="timeProvider">Clock; UTC real time when null.</param>
    public EfCoreTokenDenylist(IServiceScopeFactory scopeFactory, PubSoft.Core.IIdGenerator idGenerator, TimeProvider? timeProvider = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _idGenerator = idGenerator ?? throw new ArgumentNullException(nameof(idGenerator));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async ValueTask DenyAsync(TokenDenyEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        CheckId(entry.TokenId);
        if (entry.Subject is { Length: > MaxFieldLength } || entry.Reason is { Length: > MaxFieldLength })
            throw new ArgumentException("Subject and Reason must be <=256 characters.", nameof(entry));

        var now = _timeProvider.GetUtcNow();
        if (entry.ExpiresAt <= now)
            return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var existing = await context.Set<TokenDenylistRecord>()
            .FirstOrDefaultAsync(record => record.TokenId == entry.TokenId, cancellationToken) // pubsoft:exempt PUBSOFT004 "EF Core cannot translate string.Equals with StringComparison - == is the translatable form"
            .ConfigureAwait(false);
        if (existing is not null)
        {
            existing.Subject = entry.Subject;
            existing.Reason = entry.Reason;
            existing.DeniedAt = now;
            existing.ExpiresAt = entry.ExpiresAt;
        }
        else
        {
            context.Set<TokenDenylistRecord>().Add(new TokenDenylistRecord
            {
                Id = _idGenerator.NextId(),
                TokenId = entry.TokenId,
                Subject = entry.Subject,
                Reason = entry.Reason,
                DeniedAt = now,
                ExpiresAt = entry.ExpiresAt
            });
        }

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear();
            var winner = await context.Set<TokenDenylistRecord>()
                .FirstOrDefaultAsync(record => record.TokenId == entry.TokenId, cancellationToken) // pubsoft:exempt PUBSOFT004 "EF Core cannot translate string.Equals with StringComparison - == is the translatable form"
                .ConfigureAwait(false);
            if (winner is null)
                throw;
            winner.Subject = entry.Subject;
            winner.Reason = entry.Reason;
            winner.DeniedAt = now;
            winner.ExpiresAt = entry.ExpiresAt;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsDeniedAsync(string tokenId, CancellationToken cancellationToken = default)
    {
        CheckId(tokenId);
        var now = _timeProvider.GetUtcNow();
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TDbContext>();
        return await context.Set<TokenDenylistRecord>()
            .AnyAsync(record => record.TokenId == tokenId && record.ExpiresAt > now, cancellationToken) // pubsoft:exempt PUBSOFT004 "EF Core cannot translate string.Equals with StringComparison - == is the translatable form"
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes entries whose deny window has passed. Not part of the contract - hosts call
    /// this on their own schedule for retention hygiene.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask<int> PruneExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<TDbContext>();
        return await context.Set<TokenDenylistRecord>()
            .Where(record => record.ExpiresAt <= now)
            .ExecuteDeleteAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static void CheckId(string tokenId)
    {
        if (string.IsNullOrEmpty(tokenId) || tokenId.Length > MaxTokenIdLength)
            throw new ArgumentException("TokenId must be non-empty and <=128 characters.", nameof(tokenId));
    }
}
