using PubSoft.Security;
using PubSoft.Security.Redis;

if (!typeof(INonceStore).IsAssignableFrom(typeof(RedisNonceStore)))
    throw new InvalidOperationException("Redis nonce adapter does not implement INonceStore.");

var options = new RedisNonceStoreOptions();
if (options.Database != -1 || options.KeyPrefix != "pubsoft:security:nonce:")
    throw new InvalidOperationException("Redis nonce options consumer check failed.");

Console.WriteLine("Isolated PubSoft.Security.Redis consumer smoke check passed.");