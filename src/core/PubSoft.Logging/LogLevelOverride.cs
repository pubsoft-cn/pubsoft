namespace PubSoft.Logging;

/// <summary>
/// Per-source level override for <see cref="AppLogger.LevelOverrides"/>: entries whose source starts
/// with <see cref="SourcePrefix"/> (ordinal, case-sensitive) are filtered against <see cref="MinLevel"/>
/// instead of the global minimum. Longest matching prefix wins; typical use is quieting noisy
/// namespaces (<c>Microsoft</c> -> Warn) or un-muting one subsystem (<c>Microsoft.AspNetCore.Hosting</c> -> Info).
/// </summary>
public sealed record LogLevelOverride
{
    /// <summary>Creates the rule; prefix must be non-empty and the level defined.</summary>
    public LogLevelOverride(string sourcePrefix, AppLogLevel minLevel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePrefix);
        if (!Enum.IsDefined(minLevel))
            throw new ArgumentOutOfRangeException(nameof(minLevel));

        SourcePrefix = sourcePrefix;
        MinLevel = minLevel;
    }

    /// <summary>Ordinal case-sensitive source prefix.</summary>
    public string SourcePrefix { get; }
    /// <summary>Minimum level for matching sources.</summary>
    public AppLogLevel MinLevel { get; }
}
