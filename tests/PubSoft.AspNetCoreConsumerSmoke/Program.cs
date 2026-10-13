using System.Text.Json;
using System.Security.Claims;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.AspNetCore;
using PubSoft.AspNetCore.Authorization;
using PubSoft.AspNetCore.Authentication;
using PubSoft.Authentication;
using PubSoft.Authorization;
using PubSoft.Domain;
using PubSoft.Monitoring;

var builder = WebApplication.CreateSlimBuilder();
builder.Services.AddSingleton(new HealthCheckAggregator([]));
builder.Services.AddPubSoftRequestSaveChanges();
builder.Services.AddPubSoftRateLimitPolicy(options =>
{
    options.Name = "consumer-login-failures";
    options.RequestsPerWindow = 1;
    options.CountFailuresOnly = true;
    options.AppliesTo = context => context.Request.Path.StartsWithSegments("/api/login");
    options.IdentifierSelector = context => context.Connection.RemoteIpAddress?.ToString();
});
builder.Services.AddAuthenticatedSubjectAccessor("sub", "consumer-issuer", "tenant_id");
builder.Services.AddSingleton<ITenantContext>(new ConsumerTenantContext(7));
builder.Services.AddSingleton(new PermissionDefinitionRegistry([new ConsumerPermissionDefinitions()]));
builder.Services.AddSingleton<IPermissionGrantProvider, ConsumerPermissionGrants>();
builder.Services.AddScoped<PermissionAuthorizationService>();
builder.Services.AddControllers().AddPubSoftPermissionAuthorization();
await using var app = builder.Build();
app.MapPubSoftHealthEndpoint();
app.MapControllers();
app.MapGet("/private", () => Results.Ok("authorized"))
    .WithMetadata(new RequirePermissionAttribute("records", PermissionFlags.Query))
    .AddPubSoftPermissionAuthorization();

var routeBuilder = (IEndpointRouteBuilder)app;
var endpoint = routeBuilder.DataSources
    .SelectMany(dataSource => dataSource.Endpoints)
    .OfType<RouteEndpoint>()
    .Single(route => route.RoutePattern.RawText == HealthEndpointRouteBuilderExtensions.DefaultPath);
var context = new DefaultHttpContext { RequestServices = app.Services };
context.Response.Body = new MemoryStream();
await endpoint.RequestDelegate!(context);

context.Response.Body.Position = 0;
using var response = await JsonDocument.ParseAsync(context.Response.Body);
if (context.Response.StatusCode != StatusCodes.Status200OK ||
    response.RootElement.GetProperty("status").GetString() != "Healthy")
    throw new InvalidOperationException("ASP.NET Core health endpoint consumer check failed.");

await using var requestScope = app.Services.CreateAsyncScope();
var httpContextAccessor = requestScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
httpContextAccessor.HttpContext = new DefaultHttpContext
{
    User = new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim("sub", "42", ClaimValueTypes.String, "consumer-issuer"),
         new Claim("tenant_id", "7", ClaimValueTypes.String, "consumer-issuer")], "smoke"))
};
var principalSnapshot = requestScope.ServiceProvider.GetRequiredService<IPrincipalSnapshotAccessor>().GetCurrent();
if (principalSnapshot?.IsAuthenticated != true ||
    principalSnapshot.Claims.Single(claim => claim.Type == "sub").Value != "42")
    throw new InvalidOperationException("ASP.NET principal snapshot consumer check failed.");
var authenticatedSubject = requestScope.ServiceProvider.GetRequiredService<IAuthenticatedSubjectAccessor>().GetCurrent();
if (authenticatedSubject != new AuthenticatedSubject(42, 7))
    throw new InvalidOperationException("ASP.NET authenticated subject consumer check failed.");

var permissionEndpoint = routeBuilder.DataSources
    .SelectMany(dataSource => dataSource.Endpoints)
    .OfType<RouteEndpoint>()
    .Single(route => route.RoutePattern.RawText == "/private");
var trustedTenantPrincipal = new ClaimsPrincipal(new ClaimsIdentity(
[
    new Claim("sub", "42", ClaimValueTypes.String, "consumer-issuer"),
    new Claim("tenant_id", "7", ClaimValueTypes.String, "consumer-issuer")
], "smoke"));
var permissionContext = new DefaultHttpContext
{
    RequestServices = requestScope.ServiceProvider,
    User = trustedTenantPrincipal
};
permissionContext.SetEndpoint(permissionEndpoint);
permissionContext.Response.Body = new MemoryStream();
httpContextAccessor.HttpContext = permissionContext;
await permissionEndpoint.RequestDelegate!(permissionContext);
if (permissionContext.Response.StatusCode != StatusCodes.Status200OK)
    throw new InvalidOperationException("ASP.NET explicit permission grant was not accepted.");

permissionContext = new DefaultHttpContext
{
    RequestServices = requestScope.ServiceProvider,
    User = new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim("sub", "42", ClaimValueTypes.String, "consumer-issuer"),
         new Claim("tenant_id", "8", ClaimValueTypes.String, "consumer-issuer")], "smoke"))
};
permissionContext.SetEndpoint(permissionEndpoint);
httpContextAccessor.HttpContext = permissionContext;
await permissionEndpoint.RequestDelegate!(permissionContext);
if (permissionContext.Response.StatusCode != StatusCodes.Status403Forbidden)
    throw new InvalidOperationException("ASP.NET tenant mismatch was not rejected.");

permissionContext = new DefaultHttpContext
{
    RequestServices = requestScope.ServiceProvider,
    User = new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim("sub", "42", ClaimValueTypes.String, "untrusted-issuer"),
         new Claim("tenant_id", "7", ClaimValueTypes.String, "consumer-issuer")], "smoke"))
};
permissionContext.SetEndpoint(permissionEndpoint);
httpContextAccessor.HttpContext = permissionContext;
await permissionEndpoint.RequestDelegate!(permissionContext);
if (permissionContext.Response.StatusCode != StatusCodes.Status401Unauthorized)
    throw new InvalidOperationException("ASP.NET untrusted issuer was not rejected.");

var mvcEndpoint = routeBuilder.DataSources
    .SelectMany(dataSource => dataSource.Endpoints)
    .OfType<RouteEndpoint>()
    .Single(route => route.RoutePattern.RawText?.Trim('/') == "mvc-private");
var mvcContext = new DefaultHttpContext
{
    RequestServices = requestScope.ServiceProvider,
    User = trustedTenantPrincipal
};
mvcContext.SetEndpoint(mvcEndpoint);
mvcContext.Response.Body = new MemoryStream();
httpContextAccessor.HttpContext = mvcContext;
await mvcEndpoint.RequestDelegate!(mvcContext);
if (mvcContext.Response.StatusCode != StatusCodes.Status200OK)
    throw new InvalidOperationException("MVC explicit permission grant was not accepted.");

var saveChanges = requestScope.ServiceProvider.GetRequiredService<PubSoftRequestSaveChanges>();
var saveCount = 0;
saveChanges.Register(_ =>
{
    saveCount++;
    return Task.CompletedTask;
});
var requestContext = new DefaultHttpContext { RequestServices = requestScope.ServiceProvider };
var saveMiddleware = new PubSoftRequestSaveChangesMiddleware(_ => Task.CompletedTask);
await saveMiddleware.InvokeAsync(requestContext);
if (saveCount != 1)
    throw new InvalidOperationException("ASP.NET Core request save consumer check failed.");

var ratePolicyMiddleware = new PubSoftRateLimitPolicyMiddleware(
    context =>
    {
        context.Response.StatusCode = int.Parse(context.Request.Query["status"].ToString());
        return Task.CompletedTask;
    },
    app.Services.GetServices<PubSoftRateLimitPolicy>());
var rateSuccess = CreateRatePolicyContext("200");
await ratePolicyMiddleware.InvokeAsync(rateSuccess);
var rateFailure = CreateRatePolicyContext("401");
await ratePolicyMiddleware.InvokeAsync(rateFailure);
var rateLimited = CreateRatePolicyContext("401");
await ratePolicyMiddleware.InvokeAsync(rateLimited);
if (rateLimited.Response.StatusCode != StatusCodes.Status429TooManyRequests)
    throw new InvalidOperationException("ASP.NET failure-only rate policy consumer check failed.");

Console.WriteLine("Isolated PubSoft.AspNetCore consumer smoke check passed.");

static DefaultHttpContext CreateRatePolicyContext(string statusCode)
{
    var context = new DefaultHttpContext();
    context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.25");
    context.Request.Path = "/api/login";
    context.Request.QueryString = new QueryString($"?status={statusCode}");
    return context;
}

sealed class ConsumerTenantContext(long? tenantId) : ITenantContext
{
    public long? TenantId { get; } = tenantId;
}

sealed class ConsumerPermissionDefinitions : IPermissionDefinitionProvider
{
    public IReadOnlyList<PermissionDefinition> GetDefinitions() =>
    [
        new PermissionDefinition
        {
            ModuleKey = "records",
            Flags = PermissionFlags.Query,
            DisplayName = "Read records"
        }
    ];
}

sealed class ConsumerPermissionGrants : IPermissionGrantProvider
{
    public Task<IReadOnlyList<PermissionGrant>> GetGrantsAsync(
        long subjectId,
        long? tenantId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PermissionGrant>>(
            subjectId == 42 && tenantId == 7
                  ? [new PermissionGrant(7, "records", PermissionFlags.Query)]
                : []);
}

[ApiController]
[Route("mvc-private")]
public sealed class ConsumerPermissionController : ControllerBase
{
    [HttpGet]
    [RequirePermission("records", PermissionFlags.Query)]
    public IActionResult Get() => Ok("authorized");
}