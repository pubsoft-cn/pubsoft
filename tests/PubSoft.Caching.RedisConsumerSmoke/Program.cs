using PubSoft.Caching;
using PubSoft.Caching.Redis;

if (!typeof(ICache).IsAssignableFrom(typeof(RedisCache)))
    throw new InvalidOperationException("Redis cache adapter does not implement the provider-neutral cache contract.");

Console.WriteLine("Isolated PubSoft.Caching.Redis consumer smoke check passed.");