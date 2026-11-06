namespace PubSoft.Domain;

public interface IMultiTenant
{
    long? TenantId { get; set; }
}
