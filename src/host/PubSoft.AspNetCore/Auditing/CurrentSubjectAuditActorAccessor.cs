using PubSoft.Auditing;
using PubSoft.Authentication;

namespace PubSoft.AspNetCore.Auditing;

/// <summary>Default <see cref="IAuditActorAccessor"/> bridging the registered <see cref="ICurrentSubjectAccessor"/>.</summary>
internal sealed class CurrentSubjectAuditActorAccessor(ICurrentSubjectAccessor subjectAccessor) : IAuditActorAccessor
{
    public AuditActorContext? GetCurrentActor()
    {
        var subject = subjectAccessor.GetCurrent();
        return subject is null
            ? null
            : new AuditActorContext(subject.SubjectId.ToString(System.Globalization.CultureInfo.InvariantCulture), subject.Name);
    }
}
