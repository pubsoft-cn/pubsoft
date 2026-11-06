namespace PubSoft.Domain;

/// <summary>
/// A scoped participant that persists buffered changes when a host-level save point runs
/// (for example end-of-request save coordination). Implementations are discovered by the
/// host adapter and saved once, sequentially; ordering across participants is not atomic.
/// </summary>
public interface IPubSoftSaveParticipant
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
