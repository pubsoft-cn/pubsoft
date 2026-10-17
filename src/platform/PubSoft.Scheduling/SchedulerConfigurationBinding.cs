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

        if (section["LoopInterval"] is { } loop && TimeSpan.TryParse(loop, out var loopInterval))
            options.LoopInterval = loopInterval;
        if (section["ErrorDelay"] is { } delay && TimeSpan.TryParse(delay, out var errorDelay))
            options.ErrorDelay = errorDelay;
        if (section["MaxConcurrentJobs"] is { } max && int.TryParse(max, out var maxConcurrent))
            options.MaxConcurrentJobs = maxConcurrent;

        foreach (var jobSection in section.GetSection("Jobs").GetChildren())
            options.Jobs.Add(BindJob(jobSection));

        return options;
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

    /// <summary>Materializes a configuration subtree into a <see cref="JsonElement"/>.</summary>
    internal static JsonElement ToJsonElement(IConfiguration section)
        => JsonSerializer.SerializeToElement(ToJsonNode(section));

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
            foreach (var child in children.OrderBy(c => int.Parse(c.Key)))
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
}
