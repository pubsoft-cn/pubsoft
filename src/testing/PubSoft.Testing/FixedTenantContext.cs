using PubSoft.Domain;

namespace PubSoft.Testing;

/// <summary>Immutable <see cref="ITenantContext"/> returning a fixed tenant for tests.</summary>
/// <param name="TenantId">Fixed tenant id.</param>
public sealed record FixedTenantContext(long? TenantId) : ITenantContext;
