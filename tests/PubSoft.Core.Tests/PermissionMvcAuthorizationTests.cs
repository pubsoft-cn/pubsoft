using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using PubSoft.AspNetCore.Authorization;
using PubSoft.Authentication;
using PubSoft.Authorization;
using PubSoft.Domain;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class PermissionMvcAuthorizationTests
{
    [Fact]
    public async Task MvcFilterAllowsExplicitGrantedPermission()
    {
        var filter = CreateFilter(new AuthenticatedSubject(42, 7), 7, true);
        var context = CreateContext(new RequirePermissionAttribute("records", PermissionFlags.Query));

        await filter.OnAuthorizationAsync(context);

        Assert.Null(context.Result);
    }

    [Fact]
    public async Task MvcFilterRejectsMissingSubjectAndTenantMismatch()
    {
        var missingSubject = CreateContext(new RequirePermissionAttribute("records", PermissionFlags.Query));
        await CreateFilter(null, 7, true).OnAuthorizationAsync(missingSubject);
        Assert.Equal(StatusCodes.Status401Unauthorized, Assert.IsType<StatusCodeResult>(missingSubject.Result).StatusCode);

        var mismatchedTenant = CreateContext(new RequirePermissionAttribute("records", PermissionFlags.Query));
        await CreateFilter(new AuthenticatedSubject(42, 8), 7, true).OnAuthorizationAsync(mismatchedTenant);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<StatusCodeResult>(mismatchedTenant.Result).StatusCode);
    }

    [Fact]
    public async Task MvcFilterLeavesUnannotatedActionsUnchangedAndRejectsMissingGrant()
    {
        var unannotated = CreateContext();
        await CreateFilter(null, 7, false).OnAuthorizationAsync(unannotated);
        Assert.Null(unannotated.Result);

        var denied = CreateContext(new RequirePermissionAttribute("records", PermissionFlags.Query));
        await CreateFilter(new AuthenticatedSubject(42, 7), 7, false).OnAuthorizationAsync(denied);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<StatusCodeResult>(denied.Result).StatusCode);
    }

    [Fact]
    public async Task MvcFilterExemptsMarkedActionsWithoutSubject()
    {
        var exempt = CreateContext(new PermissionExemptAttribute());
        await CreateFilter(null, 7, false).OnAuthorizationAsync(exempt);
        Assert.Null(exempt.Result);

        // Exemption wins over requirement metadata on the same endpoint.
        var both = CreateContext(
            new PermissionExemptAttribute(),
            new RequirePermissionAttribute("records", PermissionFlags.Query));
        await CreateFilter(null, 7, false).OnAuthorizationAsync(both);
        Assert.Null(both.Result);
    }

    [Fact]
    public async Task MvcFilterExemptsAllowAnonymousMetadata()
    {
        var anonymous = CreateContext(new AllowAnonymousAttribute());
        await CreateFilter(null, 7, false).OnAuthorizationAsync(anonymous);
        Assert.Null(anonymous.Result);
    }

    [Fact]
    public async Task MvcFilterUsesClientCodeFromRequirement()
    {
        var definitions = new PermissionDefinitionRegistry(
        [
            new TestPermissionProvider("shop")
        ]);
        var service = new PermissionAuthorizationService(definitions, new ClientScopedGrantProvider());
        var filter = new PermissionMvcAuthorizationFilter(
            new TestSubjectAccessor(new AuthenticatedSubject(42, 7)),
            new TestTenantContext(7),
            service);
        var shop = CreateContext(new RequirePermissionAttribute("records", PermissionFlags.Query, "shop"));
        var other = CreateContext(new RequirePermissionAttribute("records", PermissionFlags.Query, "other"));

        await filter.OnAuthorizationAsync(shop);
        await filter.OnAuthorizationAsync(other);

        Assert.Null(shop.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<StatusCodeResult>(other.Result).StatusCode);
    }

    private static PermissionMvcAuthorizationFilter CreateFilter(
        AuthenticatedSubject? subject,
        long? currentTenantId,
        bool granted)
        => CreateFilter(subject, currentTenantId,
            granted ? [new PermissionGrant(currentTenantId, "records", PermissionFlags.Query)] : []);

    private static PermissionMvcAuthorizationFilter CreateFilter(
        AuthenticatedSubject? subject,
        long? currentTenantId,
        IReadOnlyList<PermissionGrant> grants)
    {
        var definitions = new PermissionDefinitionRegistry(
        [
            new TestPermissionProvider()
        ]);
        var grantProvider = new TestPermissionGrantProvider(grants);
        return new PermissionMvcAuthorizationFilter(
            new TestSubjectAccessor(subject),
            new TestTenantContext(currentTenantId),
            new PermissionAuthorizationService(definitions, grantProvider));
    }

    private static AuthorizationFilterContext CreateContext(params object[] metadata)
    {
        var action = new ActionDescriptor { EndpointMetadata = metadata };
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), action);
        return new AuthorizationFilterContext(actionContext, []);
    }

    private sealed class TestSubjectAccessor(AuthenticatedSubject? subject) : IAuthenticatedSubjectAccessor
    {
        public AuthenticatedSubject? GetCurrent() => subject;
    }

    private sealed class TestTenantContext(long? tenantId) : ITenantContext
    {
        public long? TenantId { get; } = tenantId;
    }

    private sealed class TestPermissionProvider(string clientCode = "") : IPermissionDefinitionProvider
    {
        public IReadOnlyList<PermissionDefinition> GetDefinitions() =>
        [
            new PermissionDefinition
            {
                ClientCode = clientCode,
                ModuleKey = "records",
                Flags = PermissionFlags.Query,
                DisplayName = "Read records"
            }
        ];
    }

    private sealed class ClientScopedGrantProvider : IPermissionGrantProvider
    {
        public Task<IReadOnlyList<PermissionGrant>> GetGrantsAsync(
            long subjectId,
            long? tenantId,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PermissionGrant>>([]);

        public Task<IReadOnlyList<PermissionGrant>> GetGrantsAsync(
            long subjectId,
            long? tenantId,
            string clientCode,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<PermissionGrant>>(
                clientCode == "shop"
                    ? [new PermissionGrant(tenantId, "records", PermissionFlags.Query, clientCode: clientCode)]
                    : []);
    }

    private sealed class TestPermissionGrantProvider(IReadOnlyList<PermissionGrant> grants) : IPermissionGrantProvider
    {
        public Task<IReadOnlyList<PermissionGrant>> GetGrantsAsync(
            long subjectId,
            long? tenantId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(grants);
    }
}