using Microsoft.Extensions.Configuration;

namespace PubSoft.DependencyInjection;

/// <summary>Configuration read helpers.</summary>
public static class ConfigurationExtensions
{
    /// <summary>
    /// Returns the configuration value, or null when the key is absent, empty or
    /// whitespace. <c>configuration["key"] ?? fallback</c> silently accepts empty
    /// strings - secrets and endpoints set-but-empty otherwise pass validation as
    /// "configured" and fail downstream at first use.
    /// </summary>
    public static string? GetNonEmpty(this IConfiguration configuration, string key)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(key);
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
