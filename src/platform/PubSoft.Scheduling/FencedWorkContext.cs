namespace PubSoft.Scheduling;

/// <summary>Context handed to fenced work: the acquired lock identity and its fencing token.</summary>
/// <param name="LockKey">The distributed-lock key that was acquired.</param>
/// <param name="FencingToken">
/// Monotonic token from a fencing-capable lock implementation (<see cref="PubSoft.Caching.IFencedLockHandle"/>);
/// null when the registered <see cref="PubSoft.Caching.IDistributedLock"/> does not fence (e.g. in-process).
/// Pass it to the protected resource so stale writers are rejected.
/// </param>
/// <param name="StartedAtUtc">When this execution acquired the lock.</param>
public sealed record FencedWorkContext(string LockKey, long? FencingToken, DateTimeOffset StartedAtUtc);
