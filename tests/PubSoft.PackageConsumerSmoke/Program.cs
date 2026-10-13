using PubSoft.Caching;
using PubSoft.Authentication.Jwt;
using PubSoft.Authentication.Jwt.AspNetCore;
using PubSoft.Authentication;
using PubSoft.Authorization;
using PubSoft.AspNetCore;
using PubSoft.Auditing;
using PubSoft.Auditing.Outbox;
using PubSoft.Core;
using PubSoft.Cryptography;
using PubSoft.Domain;
using PubSoft.EntityFrameworkCore;
using PubSoft.EntityFrameworkCore.Auditing;
using PubSoft.EntityFrameworkCore.Migrations;
using PubSoft.Events;
using PubSoft.Logging;
using PubSoft.Messaging.Outbox;
using PubSoft.Messaging;
using PubSoft.Mapping;
using PubSoft.Monitoring;
using PubSoft.Monitoring.Scheduling;
using PubSoft.MultiTenancy;
using PubSoft.Scheduling;
using PubSoft.Scheduling.EntityFrameworkCore;
using PubSoft.Scheduling.Http;
using PubSoft.Tokens;
using PubSoft.Notifications;
using PubSoft.Resilience;
using PubSoft.Resilience.Http;
using PubSoft.Security;
using System.Security.Cryptography;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var keyRing = new SmokeKeyRing();
var encryptor = new AesGcmEncryptor(keyRing);
const string secret = "package consumer smoke";
if (encryptor.Decrypt(encryptor.Encrypt(secret)) != secret)
    throw new InvalidOperationException("Cryptography package smoke check failed.");
using (var credentials = new CredentialEncryptionService(encryptor, "explicit-legacy-key"))
{
    var encrypted = credentials.Encrypt(secret, "tenant-1:credential-1"u8);
    if (credentials.Decrypt(encrypted, "tenant-1:credential-1"u8).Plaintext != secret)
        throw new InvalidOperationException("Credential encryption package smoke check failed.");
}

var hash = PasswordHasher.Hash("smoke-password", 100_000);
if (!PasswordHasher.Verify("smoke-password", hash))
    throw new InvalidOperationException("Security package smoke check failed.");

if (new DataMaskingService().MaskPhoneNumber("12345678") != "****5678")
    throw new InvalidOperationException("Data masking package smoke check failed.");

var response = ApiResponse<string>.Success("ok");
if (response.Data != "ok" || Money.FromFen(100).Fen != 100)
    throw new InvalidOperationException("Core package smoke check failed.");

var permissions = new PermissionDefinitionRegistry(
[
    new ConsumerPermissionProvider()
]);
if (permissions.GetByApiPath("GET", "/api/records/7").Single().Flags != PermissionFlags.Query)
    throw new InvalidOperationException("Authorization package smoke check failed.");
if (PermissionRequirementDeriver.DeriveFlags("POST", "ApproveRecord") != PermissionFlags.Audit)
    throw new InvalidOperationException("Permission derivation smoke check failed.");

var principalSnapshot = new PrincipalSnapshot(true,
    [new PrincipalClaim("sub", "consumer-user", "http://www.w3.org/2001/XMLSchema#string", "consumer-issuer")]);
if (!principalSnapshot.IsAuthenticated || principalSnapshot.Claims.Single().Value != "consumer-user")
    throw new InvalidOperationException("Authentication principal snapshot smoke check failed.");

using (var jwtKey = JwtSigning.CreateRsaKey("consumer-key"))
{
    var jwt = JwtSigning.CreateAccessToken(jwtKey, "consumer-issuer", "consumer-api",
        new Dictionary<string, string> { ["sub"] = "consumer-user" }, TimeSpan.FromMinutes(5));
    if (JwtSigning.ValidateAccessToken(jwt, "consumer-issuer", "consumer-api", jwtKey)["sub"] != "consumer-user")
        throw new InvalidOperationException("Authentication JWT package smoke check failed.");

    var jwtServices = new ServiceCollection();
    jwtServices.AddAuthentication().AddPubSoftJwtBearer(options =>
    {
        options.Issuer = "consumer-issuer";
        options.Audience = "consumer-api";
        options.JwksJson = JsonSerializer.Serialize(JwtSigning.ExportJwks(jwtKey), JsonSerializerOptions.Web);
    });
    using var jwtProvider = jwtServices.BuildServiceProvider();
    var bearer = jwtProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
        .Get(JwtBearerDefaults.AuthenticationScheme);
    var handler = new JwtSecurityTokenHandler { MapInboundClaims = bearer.MapInboundClaims };
    var principal = handler.ValidateToken(jwt, bearer.TokenValidationParameters, out _);
    if (principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value != "consumer-user" || bearer.MapInboundClaims)
        throw new InvalidOperationException("Authentication JWT bearer package smoke check failed.");
}

var entity = new SmokeEntity { Id = 7 };
if (entity.Id != 7)
    throw new InvalidOperationException("Domain package smoke check failed.");
if (new RoleDataScope(1, 2, "store", 3, 4, true).TenantId != 4)
    throw new InvalidOperationException("Role data scope domain smoke check failed.");

if (!typeof(PubSoftDbContext).IsAbstract)
    throw new InvalidOperationException("EF Core package smoke check failed.");
if (!typeof(RoleDataScopeModelBuilderExtensions).IsSealed)
    throw new InvalidOperationException("Role data scope EF mapping smoke check failed.");
if (!typeof(DbQueryDiagnostics).IsSealed)
    throw new InvalidOperationException("Database diagnostics package smoke check failed.");
if (!typeof(IDatabaseMigrationService).IsInterface || !typeof(EfCoreDatabaseSchemaMigrator<>).IsGenericTypeDefinition)
    throw new InvalidOperationException("Database migration package smoke check failed.");
if (!typeof(ISaveChangesHook).IsInterface)
    throw new InvalidOperationException("EF save hook package smoke check failed.");
if (!typeof(IAuditChangeCollector).IsInterface)
    throw new InvalidOperationException("Auditing package smoke check failed.");
if (!typeof(IAuditScope).IsInterface || !typeof(AuditScope).IsSealed)
    throw new InvalidOperationException("Audit scope package smoke check failed.");
if (!typeof(IAuditSnapshotSink).IsInterface || !typeof(AuditMiddleware).IsSealed)
    throw new InvalidOperationException("ASP.NET audit middleware smoke check failed.");
if (!typeof(ExceptionHandlingMiddleware).IsSealed || !typeof(DomainException).IsSealed)
    throw new InvalidOperationException("ASP.NET exception handling package smoke check failed.");
if (HealthEndpointRouteBuilderExtensions.DefaultPath != "/health")
    throw new InvalidOperationException("ASP.NET health endpoint package smoke check failed.");
if (!typeof(DevelopmentOnlyAttribute).IsSealed || !typeof(DevelopmentOnlyControllerConvention).IsSealed)
    throw new InvalidOperationException("ASP.NET development-only MVC convention smoke check failed.");
if (!typeof(IAuditActorAccessor).IsInterface || !typeof(AuditActorContext).IsSealed)
    throw new InvalidOperationException("Audit actor contract smoke check failed.");
if (!typeof(AuditSnapshotOutboxEvent).IsSealed || !typeof(OutboxAuditSnapshotSink<>).IsGenericTypeDefinition)
    throw new InvalidOperationException("Audit Outbox package smoke check failed.");
if (!typeof(AuditLogRecord).IsSealed || !typeof(EfCoreAuditSnapshotSink<>).IsGenericTypeDefinition)
    throw new InvalidOperationException("EF audit persistence package smoke check failed.");
if (!typeof(IDomainEventDispatcher).IsInterface || !typeof(DomainEventBackgroundService).IsSealed)
    throw new InvalidOperationException("Events package smoke check failed.");
if (!typeof(IOutboxPublisher).IsInterface || !typeof(OutboxMessage).IsSealed)
    throw new InvalidOperationException("Outbox package smoke check failed.");
if (!typeof(IOutboxScopeProvider).IsInterface)
    throw new InvalidOperationException("Outbox tenant scope provider smoke check failed.");
if (!typeof(IInboxMessageProcessor).IsInterface || !typeof(InboxReceipt).IsSealed)
    throw new InvalidOperationException("Inbox package smoke check failed.");
if (!typeof(IMessagePublisher).IsInterface || !typeof(IMessageHandler<>).IsInterface)
    throw new InvalidOperationException("Messaging contracts package smoke check failed.");
var messagingEnvelope = MessageEnvelope.Create("consumer-id", "consumer.event.v1", new ConsumerEvent(17), DateTimeOffset.UtcNow);
if (messagingEnvelope.DeserializePayload<ConsumerEvent>()?.Value != 17)
    throw new InvalidOperationException("Messaging envelope package smoke check failed.");
if (!typeof(InboxOutboxDispatcher).IsAbstract)
    throw new InvalidOperationException("Inbox Outbox dispatcher smoke check failed.");
if (!typeof(AppLogger).IsSealed || !typeof(ILogSink).IsInterface)
    throw new InvalidOperationException("Logging package smoke check failed.");
if (!typeof(INotificationChannel).IsInterface || !typeof(NotificationDispatcher).IsSealed)
    throw new InvalidOperationException("Notifications package smoke check failed.");
var mappedSmoke = ObjectMapper.Map<MappingSmokeSource, MappingSmokeDestination>(new MappingSmokeSource { Value = 9 });
if (mappedSmoke.Value != 9)
    throw new InvalidOperationException("Mapping package smoke check failed.");
if (!typeof(IProbeHealthCheck).IsInterface || !typeof(HttpProbeHealthCheck).IsSealed)
    throw new InvalidOperationException("Monitoring package smoke check failed.");
if (!typeof(IAppHealthCheck).IsInterface || !typeof(HealthCheckAggregator).IsSealed)
    throw new InvalidOperationException("Application health aggregation smoke check failed.");
if (!typeof(CachedHealthCheckAggregator).IsSealed)
    throw new InvalidOperationException("Cached health aggregation smoke check failed.");
if (!typeof(ParallelHealthCheckAggregator).IsSealed)
    throw new InvalidOperationException("Parallel health aggregation smoke check failed.");
if (!typeof(AlertRuleProcessor).IsSealed || !typeof(AlertConditionType).IsEnum)
    throw new InvalidOperationException("Monitoring scheduler integration smoke check failed.");
var tenantSmoke = new Tenant(1, "consumer", "Consumer Tenant");
if (tenantSmoke.Code != "consumer" || tenantSmoke.DisplayName != "Consumer Tenant")
    throw new InvalidOperationException("Multi-tenancy package smoke check failed.");
if (!typeof(IJobExecutor).IsInterface || !typeof(SchedulerEngine).IsSealed)
    throw new InvalidOperationException("Scheduling package smoke check failed.");
if (!typeof(EfCoreStateStore<>).IsGenericTypeDefinition || !typeof(SchedulerStateRecord).IsSealed)
    throw new InvalidOperationException("Scheduler EF persistence package smoke check failed.");
if (!typeof(ITokenProvider).IsInterface || !typeof(TokenDelegatingHandler).IsSealed)
    throw new InvalidOperationException("Tokens package smoke check failed.");
if (!typeof(ProbeExecutor).IsSealed || !typeof(ResilientExecutor).IsSealed)
    throw new InvalidOperationException("Scheduling HTTP package smoke check failed.");
if (!typeof(IRepository<,>).IsInterface)
    throw new InvalidOperationException("Domain repository contract smoke check failed.");

if (!new RetryPolicy().ShouldRetry(null, 1, 503))
    throw new InvalidOperationException("Resilience package smoke check failed.");

var resilienceServices = new ServiceCollection();
resilienceServices.AddHttpClient("resilience-smoke").AddPubSoftHttpResilience();
resilienceServices.AddPubSoftRateLimiting(options => options.RequestsPerWindow = 3);
using (var resilienceProvider = resilienceServices.BuildServiceProvider())
{
    var resilientClient = resilienceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("resilience-smoke");
    if (resilientClient.Timeout != Timeout.InfiniteTimeSpan)
        throw new InvalidOperationException("HTTP resilience adapter smoke check failed.");
    if (!resilienceProvider.GetRequiredService<RateLimiter>().CheckByIpAddress("192.0.2.1").IsAllowed)
        throw new InvalidOperationException("ASP.NET rate limiting adapter smoke check failed.");
}

if (new SecurityHeadersOptions().XFrameOptions != "DENY")
    throw new InvalidOperationException("ASP.NET Core adapter package smoke check failed.");
if (IpNormalizer.Normalize("::ffff:192.0.2.10") != "192.0.2.10")
    throw new InvalidOperationException("ASP.NET Core IP normalization smoke check failed.");

if (!new RateLimiter(new RateLimiterOptions { RequestsPerWindow = 1 }).CheckRateLimit("consumer").IsAllowed)
    throw new InvalidOperationException("Rate limiter package smoke check failed.");

var nonceStore = new MemoryNonceStore();
var signatureValidator = new HmacSignatureValidator(nonceStore);
var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
var nonce = "consumer-nonce";
var signature = HmacSignatureValidator.BuildSignature("consumer-secret", "GET", "/", timestamp, nonce);
if (!(await signatureValidator.ValidateAsync("consumer-app", signature, timestamp, nonce, "GET", "/", null, "consumer-secret")).IsValid)
    throw new InvalidOperationException("HMAC validator package smoke check failed.");

using var cache = new MemoryCache();
await cache.SetAsync("smoke", response);
if (await cache.GetAsync<ApiResponse<string>>("smoke") is null)
    throw new InvalidOperationException("Caching package smoke check failed.");

Console.WriteLine("Package consumer smoke check passed.");

sealed class SmokeEntity : Entity<int>;

sealed class ConsumerPermissionProvider : IPermissionDefinitionProvider
{
    public IReadOnlyList<PermissionDefinition> GetDefinitions() =>
    [
        new()
        {
            ModuleKey = "records",
            Flags = PermissionFlags.Query,
            DisplayName = "Read records",
            ApiPaths = ["GET /api/records/{recordId}"]
        }
    ];
}

sealed class MappingSmokeSource { public int Value { get; init; } }
sealed class MappingSmokeDestination { public int Value { get; set; } }
sealed record ConsumerEvent(int Value);

sealed class SmokeKeyRing : IEncryptionKeyRing
{
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    public string ActiveKeyId => "smoke-key";

    public bool TryGetKey(string keyId, out ReadOnlyMemory<byte> key)
    {
        key = keyId == ActiveKeyId ? _key : default;
        return keyId == ActiveKeyId;
    }
}