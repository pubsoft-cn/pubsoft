using Microsoft.Extensions.DependencyInjection;
using PubSoft.Security;
using Xunit;

namespace PubSoft.Security.Tests;

/// <summary>Refresh-token family rotation: rotation chain, reuse detection, revocation.</summary>
public sealed class RefreshTokenFamilyTests
{
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;

        public void Advance(TimeSpan span) => _now += span;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    private static (RefreshTokenFamilyService Service, MemoryRefreshTokenFamilyStore Store, ManualClock Clock) Create(RefreshTokenFamilyOptions? options = null)
    {
        var clock = new ManualClock();
        var store = new MemoryRefreshTokenFamilyStore(timeProvider: clock);
        return (new RefreshTokenFamilyService(store, options, clock), store, clock);
    }

    [Fact]
    public async Task IssueThenRotateReturnsSuccessorInSameFamily()
    {
        var (service, _, _) = Create();
        var issue = await service.IssueAsync("subject-1");
        Assert.StartsWith("rt_", issue.Token);
        Assert.False(string.IsNullOrEmpty(issue.FamilyId));

        var rotated = await service.RotateAsync(issue.Token);
        Assert.Equal(RefreshRotationVerdict.Rotated, rotated.Verdict);
        Assert.Equal(issue.FamilyId, rotated.FamilyId);
        Assert.NotNull(rotated.NewToken);
        Assert.NotEqual(issue.Token, rotated.NewToken);
    }

    [Fact]
    public async Task RotatedChainCanRotateRepeatedly()
    {
        var (service, _, _) = Create();
        var token = (await service.IssueAsync("subject-1")).Token;
        for (var i = 0; i < 5; i++)
        {
            var rotated = await service.RotateAsync(token);
            Assert.Equal(RefreshRotationVerdict.Rotated, rotated.Verdict);
            token = rotated.NewToken!;
        }
    }

    [Fact]
    public async Task ReuseOfConsumedTokenRevokesWholeFamily()
    {
        var (service, _, _) = Create();
        var issue = await service.IssueAsync("subject-1");
        var rotated = await service.RotateAsync(issue.Token);
        Assert.Equal(RefreshRotationVerdict.Rotated, rotated.Verdict);

        // Attacker replays the already-consumed first token.
        var replay = await service.RotateAsync(issue.Token);
        Assert.Equal(RefreshRotationVerdict.ReuseDetected, replay.Verdict);
        Assert.Null(replay.NewToken);

        // The legitimate successor is now dead too — whole family revoked.
        var successor = await service.RotateAsync(rotated.NewToken!);
        Assert.Equal(RefreshRotationVerdict.FamilyRevoked, successor.Verdict);
    }

    [Fact]
    public async Task UnknownAndMalformedTokensFailClosed()
    {
        var (service, _, _) = Create();
        Assert.Equal(RefreshRotationVerdict.Unknown, (await service.RotateAsync("rt_not-a-token")).Verdict);
        Assert.Equal(RefreshRotationVerdict.Unknown, (await service.RotateAsync(string.Empty)).Verdict);
        Assert.Equal(RefreshRotationVerdict.Unknown, (await service.RotateAsync(new string('x', 513))).Verdict);
    }

    [Fact]
    public async Task ExpiredTokenRotatesToExpired()
    {
        var (service, _, clock) = Create(new RefreshTokenFamilyOptions { TokenLifetime = TimeSpan.FromMinutes(5) });
        var issue = await service.IssueAsync("subject-1");
        clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(RefreshRotationVerdict.Expired, (await service.RotateAsync(issue.Token)).Verdict);
    }

    [Fact]
    public async Task FamilyDeadlineCapsRotation()
    {
        var (service, _, clock) = Create(new RefreshTokenFamilyOptions
        {
            TokenLifetime = TimeSpan.FromDays(10),
            FamilyMaxLifetime = TimeSpan.FromDays(20)
        });
        var issue = await service.IssueAsync("subject-1");
        clock.Advance(TimeSpan.FromDays(8));
        var first = await service.RotateAsync(issue.Token);
        Assert.Equal(RefreshRotationVerdict.Rotated, first.Verdict);

        clock.Advance(TimeSpan.FromDays(8)); // day 16; successor expiry capped at day-20 family deadline
        var second = await service.RotateAsync(first.NewToken!);
        Assert.Equal(RefreshRotationVerdict.Rotated, second.Verdict);
        Assert.True(second.NewToken is not null);

        clock.Advance(TimeSpan.FromDays(5)); // day 21 > family max
        var third = await service.RotateAsync(second.NewToken!);
        Assert.Equal(RefreshRotationVerdict.Expired, third.Verdict);
    }

    [Fact]
    public async Task RevokeFamilyBlocksFurtherRotation()
    {
        var (service, _, _) = Create();
        var issue = await service.IssueAsync("subject-1");
        await service.RevokeFamilyAsync(issue.FamilyId);
        Assert.Equal(RefreshRotationVerdict.FamilyRevoked, (await service.RotateAsync(issue.Token)).Verdict);
    }

    [Fact]
    public async Task RevokeSubjectRevokesAllFamiliesButNotOthers()
    {
        var (service, _, _) = Create();
        var a = await service.IssueAsync("subject-1");
        var b = await service.IssueAsync("subject-1");
        var other = await service.IssueAsync("subject-2");

        var revoked = await service.RevokeSubjectAsync("subject-1");
        Assert.Equal(2, revoked);
        Assert.Equal(RefreshRotationVerdict.FamilyRevoked, (await service.RotateAsync(a.Token)).Verdict);
        Assert.Equal(RefreshRotationVerdict.FamilyRevoked, (await service.RotateAsync(b.Token)).Verdict);
        Assert.Equal(RefreshRotationVerdict.Rotated, (await service.RotateAsync(other.Token)).Verdict);
    }

    [Fact]
    public async Task StoreKeepsOnlyTokenHash()
    {
        var (service, store, _) = Create();
        var issue = await service.IssueAsync("subject-1");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(issue.Token)));
        var record = await store.GetByHashAsync(hash);
        Assert.NotNull(record);
        Assert.Equal("subject-1", record.Subject);
        Assert.DoesNotContain(issue.Token, record.TokenHash, StringComparison.Ordinal);
        Assert.Equal(64, record.TokenHash.Length);
    }

    [Fact]
    public async Task MemoryStoreEnforcesCapacity()
    {
        var clock = new ManualClock();
        var store = new MemoryRefreshTokenFamilyStore(maxEntries: 2, timeProvider: clock);
        var service = new RefreshTokenFamilyService(store, null, clock);
        await service.IssueAsync("s1");
        await service.IssueAsync("s2");
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IssueAsync("s3").AsTask());
    }

    [Fact]
    public async Task MemoryStoreSweepsExpiredBeforeFailingCapacity()
    {
        var clock = new ManualClock();
        var store = new MemoryRefreshTokenFamilyStore(maxEntries: 2, timeProvider: clock);
        var service = new RefreshTokenFamilyService(store, new RefreshTokenFamilyOptions { TokenLifetime = TimeSpan.FromMinutes(1) }, clock);
        await service.IssueAsync("s1");
        await service.IssueAsync("s2");
        clock.Advance(TimeSpan.FromMinutes(2));
        await service.IssueAsync("s3"); // sweep frees capacity
    }

    [Fact]
    public void OptionsRejectInvalidBounds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RefreshTokenFamilyService(new MemoryRefreshTokenFamilyStore(), new RefreshTokenFamilyOptions { TokenLifetime = TimeSpan.Zero }));
        Assert.Throws<ArgumentException>(() => new RefreshTokenFamilyService(new MemoryRefreshTokenFamilyStore(), new RefreshTokenFamilyOptions { TokenLifetime = TimeSpan.FromDays(20), FamilyMaxLifetime = TimeSpan.FromDays(10) }));
    }

    [Fact]
    public async Task ServiceRejectsNullStoreAndBadSubjects()
    {
        Assert.Throws<ArgumentNullException>(() => new RefreshTokenFamilyService(null!));
        var (service, _, _) = Create();
        await Assert.ThrowsAsync<ArgumentException>(() => service.IssueAsync(string.Empty).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => service.IssueAsync(new string('s', 1025)).AsTask());
    }

    [Fact]
    public void MemoryRegistrationResolvesStoreAndService()
    {
        var sp = new ServiceCollection().AddPubSoftMemoryRefreshTokenFamily().BuildServiceProvider();
        Assert.IsAssignableFrom<MemoryRefreshTokenFamilyStore>(sp.GetRequiredService<IRefreshTokenFamilyStore>());
        Assert.NotNull(sp.GetRequiredService<RefreshTokenFamilyService>());
    }

    [Fact]
    public void ServiceOnlyRegistrationFailsWithoutStore()
    {
        var sp = new ServiceCollection().AddPubSoftRefreshTokenFamilyService().BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<RefreshTokenFamilyService>());
    }

    [Fact]
    public void ServiceOnlyRegistrationResolvesWithExplicitStore()
    {
        var sp = new ServiceCollection()
            .AddSingleton<IRefreshTokenFamilyStore>(new MemoryRefreshTokenFamilyStore())
            .AddPubSoftRefreshTokenFamilyService()
            .BuildServiceProvider();
        Assert.NotNull(sp.GetRequiredService<RefreshTokenFamilyService>());
    }
}
