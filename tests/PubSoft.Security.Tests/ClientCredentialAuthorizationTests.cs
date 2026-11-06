using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.AspNetCore.Authentication;
using PubSoft.Domain;
using Xunit;

// PubSoft.AspNetCore.Authentication tests — signature-delegate path

namespace PubSoft.Core.Tests;

public sealed class ClientCredentialAuthorizationTests
{
    [Fact]
    public async Task UnannotatedEndpointPassesThrough()
    {
        var context = CreateContext(services: BuildServices(null));
        var executed = false;
        await new ClientCredentialMvcAuthorizationFilter().OnActionExecutionAsync(context, Next(context, () => executed = true));
        Assert.True(executed);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task MarkedEndpointWithoutVerifierFailsClosed()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var context = CreateContext(services, new RequireClientCredentialAttribute());
        await new ClientCredentialMvcAuthorizationFilter().OnActionExecutionAsync(context, Next(context));
        Assert.Equal(StatusCodes.Status403Forbidden, ((StatusCodeResult)context.Result!).StatusCode);
    }

    [Fact]
    public async Task MissingOrInvalidHeadersReturn401()
    {
        var services = BuildServices(new AcceptAllVerifier());
        var marked = new RequireClientCredentialAttribute();

        var missing = CreateContext(services, marked);
        await new ClientCredentialMvcAuthorizationFilter().OnActionExecutionAsync(missing, Next(missing));
        Assert.Equal(401, ((StatusCodeResult)missing.Result!).StatusCode);

        var invalid = CreateContext(BuildServices(new DenyAllVerifier()), marked);
        invalid.HttpContext.Request.Headers["X-Client-Id"] = "svc";
        invalid.HttpContext.Request.Headers["X-Client-Secret"] = "wrong";
        await new ClientCredentialMvcAuthorizationFilter().OnActionExecutionAsync(invalid, Next(invalid));
        Assert.Equal(401, ((StatusCodeResult)invalid.Result!).StatusCode);
    }

    [Fact]
    public async Task ValidCredentialsAttachMachinePrincipalAndScopeTenantInsideAction()
    {
        var tenantContext = new AmbientTenantContext();
        var services = BuildServices(
            new AcceptAllVerifier(resultTenant: 42, claims: new Dictionary<string, string> { ["svc_role"] = "sync" }),
            tenantContext);
        var context = CreateContext(services, new RequireClientCredentialAttribute());
        context.HttpContext.Request.Headers["X-Client-Id"] = "billing-svc";
        context.HttpContext.Request.Headers["X-Client-Secret"] = "s3cret";

        long? tenantInsideAction = null;
        await new ClientCredentialMvcAuthorizationFilter().OnActionExecutionAsync(
            context,
            Next(context, () => tenantInsideAction = tenantContext.TenantId));

        Assert.Null(context.Result);
        var principal = context.HttpContext.User;
        Assert.Equal("billing-svc", principal.FindFirst("client_id")?.Value);
        Assert.Equal("Machine", principal.FindFirst("caller_type")?.Value);
        Assert.Equal("sync", principal.FindFirst("svc_role")?.Value);
        Assert.Equal("billing-svc", context.HttpContext.Items["PubSoft.ClientCredential.ClientId"]);
        Assert.Equal(42L, tenantInsideAction);
        Assert.Null(tenantContext.TenantId);
    }

    [Fact]
    public async Task AllowAuthenticatedUserBypassesCredentialCheck()
    {
        var services = BuildServices(new DenyAllVerifier());
        var marked = new RequireClientCredentialAttribute { AllowAuthenticatedUser = true };
        var context = CreateContext(services, marked);
        context.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "1")], "Bearer"));

        var executed = false;
        await new ClientCredentialMvcAuthorizationFilter().OnActionExecutionAsync(context, Next(context, () => executed = true));
        Assert.True(executed);
        Assert.Null(context.Result);
    }

    [Fact]
    public async Task AllowAuthenticatedUserFalseStillRequiresCredentials()
    {
        var services = BuildServices(new DenyAllVerifier());
        var marked = new RequireClientCredentialAttribute { AllowAuthenticatedUser = false };
        var context = CreateContext(services, marked);
        context.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "1")], "Bearer"));

        await new ClientCredentialMvcAuthorizationFilter().OnActionExecutionAsync(context, Next(context));
        Assert.Equal(401, ((StatusCodeResult)context.Result!).StatusCode);
    }

    [Fact]
    public async Task SignedRequestDelegatesToClientSignatureVerifier()
    {
        var verifier = new RecordingSignatureVerifier(accept: true);
        var services = new ServiceCollection()
            .AddSingleton<IClientSignatureVerifier>(verifier)
            .BuildServiceProvider(); // no plaintext verifier — signature-only host
        var context = CreateContext(services, new RequireClientCredentialAttribute());
        context.HttpContext.Request.Method = "POST";
        context.HttpContext.Request.Path = "/api/x";
        context.HttpContext.Request.Headers["X-Client-Id"] = "svc";
        context.HttpContext.Request.Headers["X-Signature"] = "sig-abc";
        context.HttpContext.Request.Headers["X-Timestamp"] = "1700000000000";
        context.HttpContext.Request.Headers["X-Nonce"] = "n1";

        var executed = false;
        await new ClientCredentialMvcAuthorizationFilter().OnActionExecutionAsync(context, Next(context, () => executed = true));

        Assert.True(executed);
        Assert.Equal("svc", context.HttpContext.User.FindFirst("client_id")?.Value);
        Assert.NotNull(verifier.Seen);
        Assert.Equal("sig-abc", verifier.Seen!.Signature);
        Assert.Equal("POST", verifier.Seen.Method);
        Assert.Equal("1700000000000", verifier.Seen.Timestamp);
        Assert.Equal("n1", verifier.Seen.Nonce);
    }

    [Fact]
    public async Task SignedRequestWithoutSignatureVerifierFailsClosed()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var context = CreateContext(services, new RequireClientCredentialAttribute());
        context.HttpContext.Request.Headers["X-Client-Id"] = "svc";
        context.HttpContext.Request.Headers["X-Signature"] = "sig-abc";
        context.HttpContext.Request.Headers["X-Timestamp"] = "1700000000000";
        context.HttpContext.Request.Headers["X-Nonce"] = "n1";

        await new ClientCredentialMvcAuthorizationFilter().OnActionExecutionAsync(context, Next(context));
        Assert.Equal(401, ((StatusCodeResult)context.Result!).StatusCode);
    }

    [Fact]
    public async Task RejectedSignatureReturns401()
    {
        var services = new ServiceCollection()
            .AddSingleton<IClientSignatureVerifier>(new RecordingSignatureVerifier(accept: false))
            .BuildServiceProvider();
        var context = CreateContext(services, new RequireClientCredentialAttribute());
        context.HttpContext.Request.Headers["X-Client-Id"] = "svc";
        context.HttpContext.Request.Headers["X-Signature"] = "bad";
        context.HttpContext.Request.Headers["X-Timestamp"] = "1700000000000";
        context.HttpContext.Request.Headers["X-Nonce"] = "n1";

        await new ClientCredentialMvcAuthorizationFilter().OnActionExecutionAsync(context, Next(context));
        Assert.Equal(401, ((StatusCodeResult)context.Result!).StatusCode);
    }

    private sealed class RecordingSignatureVerifier(bool accept) : IClientSignatureVerifier
    {
        public ClientSignatureRequest? Seen { get; private set; }
        public ValueTask<ClientCredentialVerificationResult> VerifyAsync(ClientSignatureRequest request, CancellationToken ct = default)
        {
            Seen = request;
            return new(accept
                ? ClientCredentialVerificationResult.Ok(request.ClientId)
                : ClientCredentialVerificationResult.Fail());
        }
    }

    private static ActionExecutionDelegate Next(ActionExecutingContext context, Action? inside = null)
        => () =>
        {
            inside?.Invoke();
            return Task.FromResult(new ActionExecutedContext(context, [], null!));
        };

    private static IServiceProvider BuildServices(IClientCredentialVerifier? verifier, AmbientTenantContext? tenantContext = null)
    {
        var services = new ServiceCollection();
        if (verifier is not null)
            services.AddSingleton(verifier);
        if (tenantContext is not null)
            services.AddSingleton(tenantContext);
        return services.BuildServiceProvider();
    }

    private static ActionExecutingContext CreateContext(IServiceProvider services, params object[] metadata)
    {
        var http = new DefaultHttpContext { RequestServices = services };
        var action = new ActionDescriptor { EndpointMetadata = metadata.ToList() };
        var actionContext = new ActionContext(http, new RouteData(), action);
        return new ActionExecutingContext(actionContext, [], new Dictionary<string, object?>(), controller: null!);
    }

    private sealed class AcceptAllVerifier(long? resultTenant = null, IReadOnlyDictionary<string, string>? claims = null)
        : IClientCredentialVerifier
    {
        public Task<ClientCredentialVerificationResult> VerifyAsync(string clientId, string clientSecret, CancellationToken ct = default)
            => Task.FromResult(ClientCredentialVerificationResult.Ok(clientId, resultTenant, claims));
    }

    private sealed class DenyAllVerifier : IClientCredentialVerifier
    {
        public Task<ClientCredentialVerificationResult> VerifyAsync(string clientId, string clientSecret, CancellationToken ct = default)
            => Task.FromResult(ClientCredentialVerificationResult.Fail());
    }
}
