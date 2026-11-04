using PubSoft.Scheduling;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class SchedulingWaveTests
{
    [Fact]
    public void EverySecondMatchesAnyTimestamp()
        => Assert.True(ScheduleMask.EverySecond.Matches(DateTimeOffset.UtcNow));

    [Fact]
    public void EveryMinuteMatchesOnlySecondZero()
    {
        Assert.True(ScheduleMask.EveryMinute.Matches(new DateTimeOffset(2026, 1, 1, 12, 30, 0, TimeSpan.Zero)));
        Assert.False(ScheduleMask.EveryMinute.Matches(new DateTimeOffset(2026, 1, 1, 12, 30, 15, TimeSpan.Zero)));
    }

    [Fact]
    public void DailyAtMatchesSpecificTime()
    {
        var mask = ScheduleMasks.DailyAt(14, 30, 0);
        Assert.True(mask.Matches(new DateTimeOffset(2026, 6, 15, 14, 30, 0, TimeSpan.Zero)));
        Assert.False(mask.Matches(new DateTimeOffset(2026, 6, 15, 14, 31, 0, TimeSpan.Zero)));
        Assert.False(mask.Matches(new DateTimeOffset(2026, 6, 15, 9, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void EverySecondsIntervalMasks()
    {
        var mask = ScheduleMasks.EverySeconds(15);
        Assert.True(mask.Matches(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.True(mask.Matches(new DateTimeOffset(2026, 1, 1, 0, 0, 15, TimeSpan.Zero)));
        Assert.False(mask.Matches(new DateTimeOffset(2026, 1, 1, 0, 0, 7, TimeSpan.Zero)));
    }

    [Fact]
    public async Task MemoryStateStoreRoundtrip()
    {
        var store = new MemoryStateStore();
        Assert.Null(await store.GetLastResultAsync("job1", CancellationToken.None));
        await store.UpdateStateAsync("job1", new JobResult { Success = true, StatusCode = 200 }, CancellationToken.None);
        var last = await store.GetLastResultAsync("job1", CancellationToken.None);
        Assert.NotNull(last);
        Assert.True(last.Success);
        Assert.Equal(200, last.StatusCode);
    }

    [Fact]
    public async Task MemoryStateStoreCustomStateExpiry()
    {
        var store = new MemoryStateStore();
        await store.SetCustomStateAsync("k1", "v1", null, CancellationToken.None);
        Assert.Equal("v1", await store.GetCustomStateAsync<string>("k1", CancellationToken.None));
        await store.RemoveCustomStateAsync("k1", CancellationToken.None);
        Assert.Null(await store.GetCustomStateAsync<string>("k1", CancellationToken.None));
    }
}
