using System.Security.Cryptography;
using System.Text;

namespace PubSoft.Security;

/// <summary>
/// Refresh-token family rotation with reuse detection. Tokens are opaque strings keyed by
/// SHA-256 hash - raw values are never stored. Presenting an already-consumed token is a
/// leak signal: the entire family is revoked so a stolen chain member cannot keep rotating.
/// </summary>
public sealed class RefreshTokenFamilyService
{
    private readonly IRefreshTokenFamilyStore _store;
    private readonly RefreshTokenFamilyOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the service over a rotation store.</summary>
    /// <param name="store">Family store implementation.</param>
    /// <param name="options">Bounded lifetimes and lengths; defaults when null.</param>
    /// <param name="timeProvider">Clock; UTC real time when null.</param>
    public RefreshTokenFamilyService(IRefreshTokenFamilyStore store, RefreshTokenFamilyOptions? options = null, TimeProvider? timeProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? new RefreshTokenFamilyOptions();
        _options.Validate();
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Issues the first token of a new family for a subject.</summary>
    /// <param name="subject">Opaque subject string (caller-defined identity key).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask<RefreshTokenIssue> IssueAsync(string subject, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > _options.MaxSubjectLength)
            throw new ArgumentException("Subject must be non-empty and within MaxSubjectLength.", nameof(subject));

        var now = _timeProvider.GetUtcNow();
        var familyId = Guid.NewGuid().ToString("N");
        var familyExpiry = now + _options.FamilyMaxLifetime;
        var token = NewToken();
        var expiresAt = Min(now + _options.TokenLifetime, familyExpiry);
        await _store.SaveAsync(new RefreshTokenFamilyRecord(Hash(token), familyId, subject, now, expiresAt, familyExpiry, Consumed: false), cancellationToken).ConfigureAwait(false);
        return new RefreshTokenIssue(token, familyId, expiresAt);
    }

    /// <summary>
    /// Consumes a presented token and issues a successor in the same family. Reuse of a
    /// consumed token revokes the whole family; revoked/expired/unknown tokens fail closed.
    /// </summary>
    /// <param name="presentedToken">Opaque token the caller presented.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async ValueTask<RefreshRotationResult> RotateAsync(string presentedToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(presentedToken) || presentedToken.Length > _options.MaxTokenLength)
            return new RefreshRotationResult(RefreshRotationVerdict.Unknown, null, null);

        var now = _timeProvider.GetUtcNow();
        var record = await _store.GetByHashAsync(Hash(presentedToken), cancellationToken).ConfigureAwait(false);
        if (record is null)
            return new RefreshRotationResult(RefreshRotationVerdict.Unknown, null, null);
        if (record.TokenExpiresAt <= now || record.FamilyExpiresAt <= now)
            return new RefreshRotationResult(RefreshRotationVerdict.Expired, null, record.FamilyId);

        var consume = await _store.TryConsumeAsync(record.TokenHash, cancellationToken).ConfigureAwait(false);
        switch (consume)
        {
            case RefreshConsumeResult.FamilyRevoked:
                return new RefreshRotationResult(RefreshRotationVerdict.FamilyRevoked, null, record.FamilyId);
            case RefreshConsumeResult.AlreadyConsumed:
                await _store.RevokeFamilyAsync(record.FamilyId, record.FamilyExpiresAt, cancellationToken).ConfigureAwait(false);
                return new RefreshRotationResult(RefreshRotationVerdict.ReuseDetected, null, record.FamilyId);
        }

        var expiresAt = Min(now + _options.TokenLifetime, record.FamilyExpiresAt);
        var token = NewToken();
        await _store.SaveAsync(new RefreshTokenFamilyRecord(Hash(token), record.FamilyId, record.Subject, now, expiresAt, record.FamilyExpiresAt, Consumed: false), cancellationToken).ConfigureAwait(false);
        return new RefreshRotationResult(RefreshRotationVerdict.Rotated, token, record.FamilyId);
    }

    /// <summary>Revokes one family - e.g. explicit logout on a single device.</summary>
    /// <param name="familyId">Family to revoke.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask RevokeFamilyAsync(string familyId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(familyId))
            throw new ArgumentException("FamilyId must be non-empty.", nameof(familyId));
        return _store.RevokeFamilyAsync(familyId, _timeProvider.GetUtcNow() + _options.FamilyMaxLifetime, cancellationToken);
    }

    /// <summary>Revokes every known family of a subject - logout everywhere.</summary>
    /// <param name="subject">Subject whose families should be revoked.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public ValueTask<int> RevokeSubjectAsync(string subject, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > _options.MaxSubjectLength)
            throw new ArgumentException("Subject must be non-empty and within MaxSubjectLength.", nameof(subject));
        return _store.RevokeSubjectAsync(subject, _timeProvider.GetUtcNow() + _options.FamilyMaxLifetime, cancellationToken);
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a <= b ? a : b;

    private static string NewToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return "rt_" + Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
