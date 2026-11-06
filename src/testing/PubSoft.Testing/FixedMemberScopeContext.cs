using PubSoft.Domain;

namespace PubSoft.Testing;


/// <summary>Immutable <see cref="IMemberScopeContext"/> returning a fixed member scope for tests.</summary>
/// <param name="MemberId">Fixed member id.</param>
/// <param name="IsMemberScoped">Whether the scope is member-scoped.</param>
public sealed record FixedMemberScopeContext(long? MemberId, bool IsMemberScoped = true) : IMemberScopeContext;
