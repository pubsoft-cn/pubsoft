namespace PubSoft.Domain;


public interface ITenantContext
{
    long? TenantId { get; }
}
