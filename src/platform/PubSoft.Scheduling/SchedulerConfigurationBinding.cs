using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;

namespace PubSoft.Scheduling;

/// <summary>
/// Binds <see cref="SchedulerOptions"/> from a configuration section without the reflection-based
/// configuration binder (this package stays AOT-compatible). <see cref="ScheduledJob.ExecutorConfig"/>
/// and <see cref="ScheduledJob.ResultProcessorConfig"/> are opaque <see cref="JsonElement"/> payloads
/// that the binder cannot produce, so job subtrees are materialized from <see cref="IConfiguration"/>
/// directly.
/// </summary>
internal static class SchedulerConfigurationBinding
{
    public static SchedulerOptions Bind(IConfiguration section)
    {
        var options = new SchedulerOptions();

        if (section["LoopInterval"] is { } loop)
            options.LoopInterval = ParseDuration(section, loop, "LoopInterval");
        if (section["ErrorDelay"] is { } delay)
            options.ErrorDelay = ParseDuration(section, delay, "ErrorDelay");
        if (section["MaxConcurrentJobs"] is { } max)
            options.MaxConcurrentJobs = ParseInt(section, max, "MaxConcurrentJobs");

        foreach (var jobSection in section.GetSection("Jobs").GetChildren())
            options.Jobs.Add(BindJob(jobSection));

        return options;
    }

    /// <summary>Materializes a configuration subtree into a <see cref="JsonElement"/>.</summary>
    internal static JsonElement ToJsonElement(IConfiguration section)
    {
        // JsonDocument DOM parse keeps this trim/AOT-safe (no reflection-based serialization).
        using var doc = JsonDocument.Parse(ToJsonNode(section)?.ToJsonString() ?? "null");
        return doc.RootElement.Clone();
    }

    private static ScheduledJob BindJob(IConfiguration section)
    {
        var job = new ScheduledJob
        {
            Name = section["Name"] ?? "",
            ExecutorType = section["ExecutorType"] ?? "Http",
        };
        if (section["Enabled"] is { } enabled)
            job.Enabled = !string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase);
        if (section["PreventOverlap"] is { } overlap)
            job.PreventOverlap = !string.Equals(overlap, "false", StringComparison.OrdinalIgnoreCase);
        if (section["Timeout"] is { } timeout)
            job.Timeout = ParseDuration(section, timeout, "Timeout");

        var retry = section.GetSection("Retry");
        if (retry.Exists())
        {
            var policy = new JobRetryPolicy();
            if (retry["MaxAttempts"] is { } maxAttempts) policy.MaxAttempts = ParseInt(section, maxAttempts, "Retry:MaxAttempts");
            if (retry["InitialDelay"] is { } initial) policy.InitialDelay = ParseDuration(section, initial, "Retry:InitialDelay");
            if (retry["BackoffFactor"] is { } factor) policy.BackoffFactor = ParseDouble(section, factor, "BackoffFactor");
            if (retry["MaxDelay"] is { } maxDelay) policy.MaxDelay = ParseDuration(section, maxDelay, "Retry:MaxDelay");
            job.Retry = policy;
        }

        var schedule = section.GetSection("Schedule");
        if (schedule.Exists())
        {
            job.Schedule = new ScheduleMask(
                ReadULong(schedule, "Seconds"),
                ReadULong(schedule, "Minutes"),
                ReadUInt(schedule, "Hours"),
                ReadUInt(schedule, "DaysOfMonth"),
                ReadUShort(schedule, "Months"),
                ReadByte(schedule, "DaysOfWeek"));
        }

        foreach (var processor in section.GetSection("ResultProcessors").GetChildren())
        {
            if (!string.IsNullOrWhiteSpace(processor.Value))
                job.ResultProcessors.Add(processor.Value);
        }

        if (section.GetSection("ExecutorConfig") is { } executorConfig && executorConfig.Exists())
            job.ExecutorConfig = ToJsonElement(executorConfig);
        if (section.GetSection("ResultProcessorConfig") is { } processorConfig && processorConfig.Exists())
            job.ResultProcessorConfig = ToJsonElement(processorConfig);

        return job;
    }

    private static JsonNode? ToJsonNode(IConfiguration config)
    {
        var children = config.GetChildren().ToList();
        if (children.Count == 0)
        {
            var value = config is IConfigurationSection s ? s.Value : null;
            if (value is null) return null;
            if (bool.TryParse(value, out var b)) return JsonValue.Create(b);
            if (long.TryParse(value, out var l)) return JsonValue.Create(l);
            if (double.TryParse(value, out var d)) return JsonValue.Create(d);
            return JsonValue.Create(value);
        }

        if (children.All(c => int.TryParse(c.Key, out _)))
        {
            var array = new JsonArray();
            foreach (var child in children.OrderBy(c => int.Parse(c.Key, System.Globalization.CultureInfo.InvariantCulture)))
                array.Add(ToJsonNode(child));
            return array;
        }

        var obj = new JsonObject();
        foreach (var child in children)
            obj[child.Key] = ToJsonNode(child);
        return obj;
    }

    private static ulong ReadULong(IConfiguration s, string key)
        => ulong.TryParse(s[key], out var v) ? v : 0;
    private static uint ReadUInt(IConfiguration s, string key)
        => uint.TryParse(s[key], out var v) ? v : 0;
    private static ushort ReadUShort(IConfiguration s, string key)
        => ushort.TryParse(s[key], out var v) ? v : (ushort)0;
    private static byte ReadByte(IConfiguration s, string key)
        => byte.TryParse(s[key], out var v) ? v : (byte)0;

    // ── strict scalar parsing ──
    // Present-but-unparsable values throw instead of silently falling back to
    // defaults — for knobs like Timeout a silent fallback inverts the intent
    // (a misspelled "5m" would leave the job unbounded).

    private static TimeSpan ParseDuration(IConfiguration section, string raw, string key)
    {
        if (TimeSpan.TryParse(raw, out var parsed))
            return parsed;
        if (TryParseDurationSuffix(raw, out var suffixed))
            return suffixed;
        throw Invalid(section, key, raw);
    }

    /// <summary>Accepts a number plus unit suffix: 500ms, 30s, 5m, 1h, 2d.</summary>
    private static bool TryParseDurationSuffix(string raw, out TimeSpan duration)
    {
        duration = default;
        var span = raw.AsSpan().Trim();
        var cut = span.Length;
        while (cut > 0 && char.IsAsciiLetter(span[cut - 1])) cut--;
        var suffix = span[cut..];
        if (suffix.Length == 0
            || !double.TryParse(span[..cut], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            return false;

        var scaled = suffix.ToString().ToLowerInvariant() switch
        {
            "ms" => TimeSpan.FromMilliseconds(value),
            "s" => TimeSpan.FromSeconds(value),
            "m" => TimeSpan.FromMinutes(value),
            "h" => TimeSpan.FromHours(value),
            "d" => TimeSpan.FromDays(value),
            _ => (TimeSpan?)null,
        };
        if (scaled is not { } result)
            return false;
        duration = result;
        return true;
    }

    private static int ParseInt(IConfiguration section, string raw, string key)
        => int.TryParse(raw, out var v) ? v : throw Invalid(section, key, raw);

    private static double ParseDouble(IConfiguration section, string raw, string key)
        => double.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : throw Invalid(section, key, raw);

    private static FormatException Invalid(IConfiguration section, string key, string raw)
        => new($"Invalid scheduler configuration '{key}' = '{raw}'"
            + (section["Name"] is { } name ? $" (job '{name}')." : ".")
            + " Durations accept TimeSpan text ('00:00:30') or a unit suffix ('500ms', '30s', '5m', '1h', '2d').");
}
