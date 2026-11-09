using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PubSoft.EntityFrameworkCore;


/// <summary>Aggregated query stats for one fingerprinted operation.</summary>
/// <param name="Operation">Logical operation label.</param>
/// <param name="QueryFingerprint">Normalized query fingerprint.</param>
/// <param name="Count">Execution count.</param>
/// <param name="ErrorCount">Failed executions.</param>
/// <param name="AverageMs">Mean duration.</param>
/// <param name="P50Ms">Median duration.</param>
/// <param name="P95Ms">95th-percentile duration.</param>
/// <param name="P99Ms">99th-percentile duration.</param>
/// <param name="MaxMs">Slowest observed duration.</param>
/// <param name="TotalMs">Cumulative duration.</param>
public sealed record QuerySnapshot(
    string Operation,
    string QueryFingerprint,
    long Count,
    long ErrorCount,
    double AverageMs,
    double P50Ms,
    double P95Ms,
    double P99Ms,
    double MaxMs,
    double TotalMs);
