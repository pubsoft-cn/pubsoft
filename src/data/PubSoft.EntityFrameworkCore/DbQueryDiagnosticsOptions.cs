using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PubSoft.EntityFrameworkCore;

/// <summary>Query-diagnostics tuning.</summary>
public sealed class DbQueryDiagnosticsOptions
{
    /// <summary>Hard cap on tracked query templates.</summary>
    public const int MaximumCapacity = 100_000;

    /// <summary>Whether diagnostics capture is enabled.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Capture sample rate 0..1.</summary>
    public double SampleRate { get; set; } = 1.0;
    /// <summary>Duration above which a query counts as slow.</summary>
    public TimeSpan SlowQueryThreshold { get; set; } = TimeSpan.FromMilliseconds(100);
    /// <summary>Tracked-template cap before eviction.</summary>
    public int MaxQueryTemplates { get; set; } = 1024;
    /// <summary>Latency samples retained per template for percentile math.</summary>
    public int MaxLatencySamplesPerTemplate { get; set; } = 512;

    internal DbQueryDiagnosticsOptions CloneAndValidate()
    {
        if (!double.IsFinite(SampleRate) || SampleRate is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(SampleRate));
        if (SlowQueryThreshold < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(SlowQueryThreshold));
        if (MaxQueryTemplates <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxQueryTemplates));
        if (MaxQueryTemplates > MaximumCapacity)
            throw new ArgumentOutOfRangeException(nameof(MaxQueryTemplates), $"The maximum supported capacity is {MaximumCapacity}.");
        if (MaxLatencySamplesPerTemplate <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxLatencySamplesPerTemplate));
        if (MaxLatencySamplesPerTemplate > MaximumCapacity)
            throw new ArgumentOutOfRangeException(nameof(MaxLatencySamplesPerTemplate), $"The maximum supported capacity is {MaximumCapacity}.");

        return new DbQueryDiagnosticsOptions
        {
            Enabled = Enabled,
            SampleRate = SampleRate,
            SlowQueryThreshold = SlowQueryThreshold,
            MaxQueryTemplates = MaxQueryTemplates,
            MaxLatencySamplesPerTemplate = MaxLatencySamplesPerTemplate
        };
    }
}
