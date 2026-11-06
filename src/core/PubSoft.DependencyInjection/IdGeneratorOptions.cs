namespace PubSoft.DependencyInjection;

/// <summary>Options for <see cref="IdGeneratorServiceCollectionExtensions.AddPubSoftIdGenerator"/>.</summary>
public sealed class IdGeneratorOptions
{
    /// <summary>The default configuration section.</summary>
    public const string SectionName = "PubSoft:IdGenerator";

    /// <summary>
    /// Replica shard 0–<see cref="PubSoft.Core.SnowflakeIdGenerator.MaxWorkerId"/>. No default on
    /// purpose: every replica must get a distinct value, and a silent shared default would
    /// collide under load.
    /// </summary>
    public long WorkerId { get; set; } = -1;
}
