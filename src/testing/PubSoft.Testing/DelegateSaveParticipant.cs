using PubSoft.Domain;

namespace PubSoft.Testing;


/// <summary><see cref="IPubSoftSaveParticipant"/> delegating to a callback, for wiring tests.</summary>
public sealed class DelegateSaveParticipant(Func<CancellationToken, Task<int>> save) : IPubSoftSaveParticipant
{
    private readonly Func<CancellationToken, Task<int>> _save =
        save ?? throw new ArgumentNullException(nameof(save));

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _save(cancellationToken);
}
