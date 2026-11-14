namespace PubSoft.EntityFrameworkCore;


/// <summary>Point-in-time diagnostics aggregate.</summary>
/// <param name="TotalQueries">Queries observed.</param>
/// <param name="TotalErrors">Failed queries.</param>
/// <param name="SlowQueryCount">Queries over the slow threshold.</param>
/// <param name="TopQueries">Per-fingerprint aggregates.</param>
public sealed record DiagnosticSnapshot(
    long TotalQueries,
    long TotalErrors,
    long SlowQueryCount,
    IReadOnlyList<QuerySnapshot> TopQueries);
