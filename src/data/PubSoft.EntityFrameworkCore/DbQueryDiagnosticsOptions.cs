using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PubSoft.EntityFrameworkCore;

public sealed class DbQueryDiagnosticsOptions
{
    public const int MaximumCapacity = 100_000;

    public bool Enabled { get; set; } = true;
    public double SampleRate { get; set; } = 1.0;
    public TimeSpan SlowQueryThreshold { get; set; } = TimeSpan.FromMilliseconds(100);
    public int MaxQueryTemplates { get; set; } = 1024;
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
