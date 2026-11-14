namespace PubSoft.Auditing;


/// <summary>Resolves the actor for the current audit scope.</summary>
public interface IAuditActorAccessor
{
    /// <summary>Returns the current actor, or null when anonymous.</summary>
    AuditActorContext? GetCurrentActor();
}
