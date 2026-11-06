using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

public static class AuditActorServiceCollectionExtensions
{
    /// <summary>Wires <see cref="IAuditActorAccessor"/> to the resolved <see cref="CurrentSubject"/>.</summary>
    public static IServiceCollection AddCurrentSubjectAuditActor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IAuditActorAccessor, CurrentSubjectAuditActorAccessor>();
        return services;
    }
}
