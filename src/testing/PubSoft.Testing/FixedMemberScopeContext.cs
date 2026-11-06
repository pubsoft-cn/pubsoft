using PubSoft.Domain;

namespace PubSoft.Testing;


/// <summary>Immutable <see cref="IMemberScopeContext"/> returning a fixed member scope for tests.</summary>
public sealed record FixedMemberScopeContext(long? MemberId, bool IsMemberScoped = true) : IMemberScopeContext;
