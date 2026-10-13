using Microsoft.Extensions.DependencyInjection;
using PubSoft.Caching;

var services = new ServiceCollection();
services.AddPubSoftMemoryCache(cleanupInterval: TimeSpan.FromMinutes(1), maxSize: 32);
using var provider = services.BuildServiceProvider();

var cache = provider.GetRequiredService<ICache>();
await cache.SetAsync("consumer:key", "value");
if (await cache.GetAsync<string>("consumer:key") != "value")
    throw new InvalidOperationException("Cache set/get consumer check failed.");

var factoryCalls = 0;
var cached = await cache.GetOrSetAsync("consumer:key", () =>
{
    Interlocked.Increment(ref factoryCalls);
    return Task.FromResult("factory");
});
if (cached != "value" || factoryCalls != 0)
    throw new InvalidOperationException("Cache get-or-set consumer check failed.");

await cache.RemoveAsync("consumer:key");
if (await cache.ExistsAsync("consumer:key"))
    throw new InvalidOperationException("Cache remove consumer check failed.");

Console.WriteLine("Isolated PubSoft.Caching consumer smoke check passed.");