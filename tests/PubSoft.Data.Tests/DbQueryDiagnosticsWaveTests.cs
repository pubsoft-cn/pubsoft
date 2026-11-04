using PubSoft.EntityFrameworkCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class DbQueryDiagnosticsWaveTests
{
    [Fact]
    public async Task ExecuteAsyncCountsQueries()
    {
        var d = new DbQueryDiagnostics();
        var r = await d.ExecuteAsync("read", "SELECT 1", _ => Task.FromResult(42));
        Assert.Equal(42, r);
        var snap = d.GetSnapshot();
        Assert.Equal(1, snap.TotalQueries);
        Assert.Equal(0, snap.TotalErrors);
        Assert.Single(snap.TopQueries);
        Assert.Equal("read", snap.TopQueries[0].Operation);
    }

    [Fact]
    public async Task ErrorsCountedAndPropagated()
    {
        var d = new DbQueryDiagnostics();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => d.ExecuteAsync<object>("write", "x", _ => throw new InvalidOperationException("boom")));
        var snap = d.GetSnapshot();
        Assert.Equal(1, snap.TotalErrors);
    }

    [Fact]
    public async Task ResetClearsStats()
    {
        var d = new DbQueryDiagnostics();
        await d.ExecuteAsync("op", "sql", _ => Task.FromResult(1));
        d.Reset();
        Assert.Equal(0, d.GetSnapshot().TotalQueries);
    }

    [Fact]
    public void OptionsValidate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => _ = new DbQueryDiagnostics(new DbQueryDiagnosticsOptions { SlowQueryThreshold = TimeSpan.FromMilliseconds(-1) }));
    }

    [Fact]
    public async Task DisabledSkipsTracking()
    {
        var d = new DbQueryDiagnostics(new DbQueryDiagnosticsOptions { Enabled = false });
        await d.ExecuteAsync("op", "sql", _ => Task.FromResult(1));
        Assert.Equal(0, d.GetSnapshot().TotalQueries);
    }
}
