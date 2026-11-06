using System.Collections.ObjectModel;

namespace PubSoft.Auditing;


public interface IAuditScope
{
    bool IsEnabled { get; }
    bool IsFinalizing { get; }

    void Initialize(AuditRequestContext request);
    void SetBusiness(
        string? category = null,
        string? action = null,
        string? targetType = null,
        string? targetId = null,
        string? targetName = null,
        string? detail = null);
    void MarkFailure(string? failureCode = null);
    AuditRequestSnapshot? Finalize(bool success, int statusCode);
}
