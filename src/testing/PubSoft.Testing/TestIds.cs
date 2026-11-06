using PubSoft.Core;

namespace PubSoft.Testing;

/// <summary>Shared Snowflake id source for tests - worker 0, single process.</summary>
public static class TestIds
{
    private static readonly SnowflakeIdGenerator GeneratorInstance = new(0);

    /// <summary>The shared generator - pass as IIdGenerator to components under test so
    /// ids are unique across every publisher/sink in the test (mirrors the DI singleton).</summary>
    public static IIdGenerator Shared => GeneratorInstance;

    /// <summary>Issues the next unique test id.</summary>
    public static long Next() => GeneratorInstance.NextId();
}
