namespace PubSoft.Domain;


public interface IMemberScopeContext
{
    long? MemberId { get; }
    bool IsMemberScoped { get; }
}
