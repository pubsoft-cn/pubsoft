using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PubSoft.Authentication;

namespace PubSoft.AspNetCore.Authentication;

public static class PrincipalSnapshotServiceCollectionExtensions
{
    public static IServiceCollection AddPrincipalSnapshotAccessor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHttpContextAccessor();
        services.TryAddScoped<IPrincipalSnapshotAccessor, HttpContextPrincipalSnapshotAccessor>();
        return services;
    }

    public static IServiceCollection AddAuthenticatedSubjectAccessor(
        this IServiceCollection services,
        string subjectClaimType,
        string trustedIssuer,
        string? tenantClaimType = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var resolver = new AuthenticatedSubjectResolver(subjectClaimType, trustedIssuer, tenantClaimType);
        services.AddPrincipalSnapshotAccessor();
        services.TryAddSingleton(resolver);
        services.TryAddScoped<IAuthenticatedSubjectAccessor, HttpContextAuthenticatedSubjectAccessor>();
        return services;
    }

    /// <summary>
    /// Registers <see cref="ICurrentSubjectAccessor"/> resolved through a
    /// <see cref="SubjectClaimDialect"/> -- the configurable-claim-names path for hosts whose
    /// token dialect names more than subject id and tenant.
    /// </summary>
    public static IServiceCollection AddCurrentSubjectAccessor(
        this IServiceCollection services,
        SubjectClaimDialect dialect,
        string trustedIssuer)
    {
        ArgumentNullException.ThrowIfNull(services);
        var resolver = new CurrentSubjectResolver(dialect, trustedIssuer);
        services.AddPrincipalSnapshotAccessor();
        services.TryAddSingleton(resolver);
        services.TryAddScoped<ICurrentSubjectAccessor, HttpContextCurrentSubjectAccessor>();
        return services;
    }
}