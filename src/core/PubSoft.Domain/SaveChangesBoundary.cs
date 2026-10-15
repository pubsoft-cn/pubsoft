namespace PubSoft.Domain;

/// <summary>
/// Scoped save-boundary contract: when activated by a host-level save point
/// (request-end middleware, or an explicit unit-of-work scope in a background
/// worker), repositories may stage writes for the boundary to flush. When no
/// activated boundary exists in the current scope, repositories must persist
/// immediately — staged writes with no boundary are silently lost.
/// </summary>
public interface ISaveChangesBoundary
{
    /// <summary>
    /// Whether a save point is armed in this scope. <c>false</c> when the boundary
    /// service merely exists (registered in DI) but no middleware/scope activated it.
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    /// Marks this scope's boundary as armed. Called by request-end middleware, or
    /// explicitly by background workers that want deferred/batched saves —
    /// after <see cref="Activate"/> the caller is responsible for invoking
    /// <see cref="SaveAllAsync"/> or staged writes are lost.
    /// </summary>
    void Activate();

    /// <summary>Registers a save callback executed once at the save point.</summary>
    void Register(Func<CancellationToken, Task> saveCallback);

    /// <summary>Enrolls <see cref="IPubSoftSaveParticipant"/> implementations once.</summary>
    void RegisterServiceParticipants(IEnumerable<IPubSoftSaveParticipant> participants);

    /// <summary>Runs all registered save callbacks sequentially.</summary>
    Task SaveAllAsync(CancellationToken cancellationToken = default);
}
