namespace PubSoft.Domain;

public interface IMultiTenant
{
    long? TenantId { get; set; }
}

public interface IMemberScoped
{
    long? MemberId { get; set; }
}

public interface ISoftDelete
{
    bool IsDeleted { get; set; }
    long? DeleterId { get; set; }
    DateTime? DeletionTime { get; set; }
}

public interface ITenantContext
{
    long? TenantId { get; }
}

public interface IMemberScopeContext
{
    long? MemberId { get; }
    bool IsMemberScoped { get; }
}
