using Microsoft.Extensions.DependencyInjection;
using PubSoft.Edge;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class RemoteEdgeSignKeyProviderTests
{
    [Fact]
    public void RegistryKeysAreServed()
    {
        var provider = Provider(ct => Task.FromResult<IReadOnlyList<EdgeSignKeyEntry>?>(
            [new("app-1", "key-1"), new("dev-2", "key-2", "md5")]));
        WaitLoaded(provider);

        Assert.Equal("key-1", provider.GetSecretKey("app-1"));
        Assert.Equal("key-2", provider.GetSecretKey("dev-2"));
        Assert.Equal(EdgeAuthScheme.Md5, provider.GetAllowedScheme("dev-2"));
        Assert.Null(provider.GetAllowedScheme("app-1"));
    }

    [Fact]
    public void LocalKeysServeAsFallbackAndRegistryWins()
    {
        var provider = Provider(ct => Task.FromResult<IReadOnlyList<EdgeSignKeyEntry>?>(
                [new("app-1", "remote-key")]),
            localKeys: new Dictionary<string, string> { ["app-1"] = "local-key", ["dev-x"] = "local-only" });
        WaitLoaded(provider);

        Assert.Equal("remote-key", provider.GetSecretKey("app-1"));
        Assert.Equal("local-only", provider.GetSecretKey("dev-x"));
    }

    [Fact]
    public void NullFetchKeepsLastGoodPool()
    {
        var calls = 0;
        var provider = Provider(ct =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<IReadOnlyList<EdgeSignKeyEntry>?>(
                calls == 1 ? [new("app-1", "key-1")] : null);
        }, refreshInterval: TimeSpan.FromMilliseconds(50));
        WaitLoaded(provider);
        Assert.Equal("key-1", provider.GetSecretKey("app-1"));

        // Wait for a timer-driven refresh returning null - the pool must survive it.
        for (var i = 0; i < 50 && Volatile.Read(ref calls) < 2; i++)
            Thread.Sleep(20);
        Assert.True(calls >= 2);
        Assert.Equal("key-1", provider.GetSecretKey("app-1"));
    }

    [Fact]
    public void FailedFetchKeepsLocalKeys()
    {
        var provider = Provider(ct => Task.FromException<IReadOnlyList<EdgeSignKeyEntry>?>(
                new HttpRequestException("registry down")),
            localKeys: new Dictionary<string, string> { ["app-1"] = "local-key" });
        Thread.Sleep(50);
        Assert.Equal("local-key", provider.GetSecretKey("app-1"));
    }

    [Fact]
    public void UnknownClientReturnsNullFailClosed()
    {
        var provider = Provider(ct => Task.FromResult<IReadOnlyList<EdgeSignKeyEntry>?>(
            [new("app-1", "key-1")]));
        WaitLoaded(provider);
        Assert.Null(provider.GetSecretKey("ghost"));
    }

    [Fact]
    public void SchemeAliasesMap()
    {
        var provider = Provider(ct => Task.FromResult<IReadOnlyList<EdgeSignKeyEntry>?>(
        [
            new("a", "k", "HMAC"), new("b", "k", "session"), new("c", "k", "SessionKey"),
            new("d", "k", "none"), new("e", "k", "bogus"),
        ]));
        WaitLoaded(provider);

        Assert.Equal(EdgeAuthScheme.Hmac, provider.GetAllowedScheme("a"));
        Assert.Equal(EdgeAuthScheme.Session, provider.GetAllowedScheme("b"));
        Assert.Equal(EdgeAuthScheme.Session, provider.GetAllowedScheme("c"));
        Assert.Equal(EdgeAuthScheme.None, provider.GetAllowedScheme("d"));
        Assert.Null(provider.GetAllowedScheme("e"));
    }

    [Fact]
    public void MissBeforeFirstFetchTriggersSyncPull()
    {
        var gate = new TaskCompletionSource<IReadOnlyList<EdgeSignKeyEntry>?>();
        var provider = Provider(ct => gate.Task);
        gate.TrySetResult([new("late", "key-late")]);
        Assert.Equal("key-late", provider.GetSecretKey("late"));
    }

    [Fact]
    public void RegistrationWiresBothInterfacesAndValidates()
    {
        var services = new ServiceCollection();
        services.AddRemoteEdgeSignKeys(o => o.FetchKeys = ct => Task.FromResult<IReadOnlyList<EdgeSignKeyEntry>?>([]));
        using var provider = services.BuildServiceProvider();
        Assert.IsType<RemoteEdgeSignKeyProvider>(provider.GetRequiredService<IEdgeSignKeyProvider>());
        Assert.IsType<RemoteEdgeSignKeyProvider>(provider.GetRequiredService<IEdgeSchemePolicy>());

        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddRemoteEdgeSignKeys(_ => { }));
    }

    private static RemoteEdgeSignKeyProvider Provider(
        Func<CancellationToken, Task<IReadOnlyList<EdgeSignKeyEntry>?>> fetch,
        IDictionary<string, string>? localKeys = null,
        TimeSpan? refreshInterval = null)
        => new(new RemoteEdgeSignKeyOptions
        {
            FetchKeys = fetch,
            LocalKeys = localKeys,
            RefreshInterval = refreshInterval ?? TimeSpan.FromMinutes(10),
        });

    private static void WaitLoaded(RemoteEdgeSignKeyProvider provider)
    {
        // The probe miss triggers the synchronous first-miss pull; a few retries cover the
        // case where the constructor's fire-and-forget refresh still holds the lock.
        for (var i = 0; i < 20; i++)
        {
            provider.GetSecretKey("__probe__");
            Thread.Sleep(10);
        }
    }
}
