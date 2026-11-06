using System.Globalization;
using System.Reflection;

namespace PubSoft.Logging;

public static class AppVersion
{
    private static readonly Lazy<(string Full, string Version, string? BuildTime)> Parsed = new(Parse, true);

    public static string Full => Parsed.Value.Full;
    public static string Version => Parsed.Value.Version;
    public static string Short => ExtractShort(Version);
    public static string? BuildTime => Parsed.Value.BuildTime;
    public static string? BuildDate => BuildTime is { Length: >= 10 } value ? value[..10].Replace("-", string.Empty) : null; // pubsoft:exempt PUBSOFT022 "char overload cannot delete characters"

    private static (string Full, string Version, string? BuildTime) Parse()
    {
        var full = GetInformationalVersion();
        return (full, full.Split('+', 2)[0].Trim(), ExtractBuildTime(full));
    }

    private static string GetInformationalVersion()
    {
        var entryAssembly = Assembly.GetEntryAssembly();
        var entryVersion = entryAssembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(entryVersion))
            return entryVersion.Trim();

        var assemblyVersion = typeof(AppVersion).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return string.IsNullOrWhiteSpace(assemblyVersion) ? "0.0.0" : assemblyVersion.Trim();
    }

    private static string ExtractShort(string version)
    {
        var parts = version.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : version;
    }

    private static string? ExtractBuildTime(string version)
    {
        var metadata = version.Split('+', 2);
        if (metadata.Length < 2)
            return null;

        var buildMetadata = metadata[1].Trim();
        if (buildMetadata.Length >= 14 && DateTime.TryParseExact(
            buildMetadata.AsSpan(0, 14), "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var timestamp))
            return timestamp.ToString("O", CultureInfo.InvariantCulture);

        if (buildMetadata.Length >= 8 && DateTime.TryParseExact(
            buildMetadata.AsSpan(0, 8), "yyyyMMdd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out timestamp))
            return timestamp.ToString("O", CultureInfo.InvariantCulture);

        return null;
    }
}