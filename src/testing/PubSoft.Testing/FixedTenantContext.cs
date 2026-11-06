using PubSoft.Domain;

namespace PubSoft.Testing;

/// <summary>Immutable <see cref="ITenantContext"/> returning a fixed tenant for tests.</summary>
public sealed record FixedTenantContext(long? TenantId) : ITenantContext;
