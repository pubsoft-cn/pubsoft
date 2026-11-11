using Microsoft.Extensions.DependencyInjection;
using PubSoft.Security;
using PubSoft.Security.Redis;
using StackExchange.Redis;
using Xunit;

namespace PubSoft.Platform.Tests;

/// <summary>Redis refresh-token family store: cross-instance rotation semantics (integration).</summary>
public sealed class RefreshTokenFamilyStoreWaveTests
{
    private const string RedisEnv = "PUBSOFT_REDIS";
    private static string? RedisConn => System.Environment.GetEnvironmentVariable(RedisEnv);

    [Fact]
    public void RegistrationRequiresMultiplexer()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentNullException>(() => services.AddPubSoftRedisRefreshTokenFamilyStore(null!));
    }

    [Fact]
    public void OptionsRejectInvalidValues()
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddPubSoftRedisRefreshTokenFamilyStore(null!, o => o.KeyPrefix = string.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddPubSoftRedisRefreshTokenFamilyStore(null!, o => o.Database = -2));
        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddPubSoftRedisRefreshTokenFamilyStore(null!, o => o.MaxSubjectLength = 0));
    }

    [Fact]
    public async Task RotateThenReuseRevokesFamilyAcrossInstances()
    {
        if (RedisConn is null) return; // needs PUBSOFT_REDIS
        await using var mux = await ConnectionMultiplexer.ConnectAsync(RedisConn);
        var options = new RedisRefreshTokenFamilyStoreOptions { KeyPrefix = $"pubsoft:rtf:{Guid.NewGuid():N}:" };
        // two store objects = two logical instances over the shared Redis
        var storeA = new RedisRefreshTokenFamilyStore(mux, options);
        var storeB = new RedisRefreshTokenFamilyStore(mux, options);
        var service = new RefreshTokenFamilyService(storeA, new RefreshTokenFamilyOptions { TokenLifetime = TimeSpan.FromMinutes(10) });
        var serviceB = new RefreshTokenFamilyService(storeB);

        var issue = await service.IssueAsync("subject-int");
        var rotated = await serviceB.RotateAsync(issue.Token); // rotate via the other instance
        Assert.Equal(RefreshRotationVerdict.Rotated, rotated.Verdict);

        var replay = await service.RotateAsync(issue.Token); // replay on first instance
        Assert.Equal(RefreshRotationVerdict.ReuseDetected, replay.Verdict);
        Assert.Equal(RefreshRotationVerdict.FamilyRevoked, (await serviceB.RotateAsync(rotated.NewToken!)).Verdict);
    }

    [Fact]
    public async Task RevokeSubjectKillsAllFamilies()
    {
        if (RedisConn is null) return; // needs PUBSOFT_REDIS
        await using var mux = await ConnectionMultiplexer.ConnectAsync(RedisConn);
        var options = new RedisRefreshTokenFamilyStoreOptions { KeyPrefix = $"pubsoft:rtf:{Guid.NewGuid():N}:" };
        var service = new RefreshTokenFamilyService(new RedisRefreshTokenFamilyStore(mux, options));

        var a = await service.IssueAsync("subject-rs");
        var b = await service.IssueAsync("subject-rs");
        var other = await service.IssueAsync("subject-other");

        Assert.Equal(2, await service.RevokeSubjectAsync("subject-rs"));
        Assert.Equal(RefreshRotationVerdict.FamilyRevoked, (await service.RotateAsync(a.Token)).Verdict);
        Assert.Equal(RefreshRotationVerdict.FamilyRevoked, (await service.RotateAsync(b.Token)).Verdict);
        Assert.Equal(RefreshRotationVerdict.Rotated, (await service.RotateAsync(other.Token)).Verdict);
    }
}
