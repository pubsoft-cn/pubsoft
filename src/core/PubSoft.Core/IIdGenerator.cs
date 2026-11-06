namespace PubSoft.Core;

/// <summary>Generates unique 64-bit ids. The default implementation is <see cref="SnowflakeIdGenerator"/>.</summary>
public interface IIdGenerator
{
    /// <summary>Issues the next unique id.</summary>
    long NextId();
}
