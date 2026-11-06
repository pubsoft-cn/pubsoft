using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace PubSoft.EntityFrameworkCore;


public sealed record DiagnosticSnapshot(
    long TotalQueries,
    long TotalErrors,
    long SlowQueryCount,
    IReadOnlyList<QuerySnapshot> TopQueries);
