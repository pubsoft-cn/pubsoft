namespace PubSoft.Domain;


/// <summary>Ambient member scope for the current flow.</summary>
public interface IMemberScopeContext
{
    /// <summary>The current member.</summary>
    long? MemberId { get; }
    /// <summary>Whether member filtering is active.</summary>
    bool IsMemberScoped { get; }
}
