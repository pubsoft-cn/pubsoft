# PublicAPI → Test Coverage Matrix

type-level coverage (global): 1111/1401 = 79%

## core/PubSoft.Contracts (5 uncovered)
   RouteSuffix
   RoutePrefix
   RouteAction
   INamingPolicy
   IsEncrypted

## core/PubSoft.Core (7 uncovered)
   PagedResultDto
   Gender
   IsPositive
   IsZero
   ISpecification
   ResultError
   IsNegative

## core/PubSoft.Cryptography (2 uncovered)
   DecryptedCredential
   AesEncryptor

## core/PubSoft.DependencyInjection (3 uncovered)
   ConventionalServiceCollectionExtensions
   ExposeServicesAttribute
   ServiceTypes

## core/PubSoft.Domain (15 uncovered)
   CreatorId
   FullAuditedAggregateRoot
   AuditedAggregateRoot
   AggregateRoot
   LastModificationTime
   CreationAuditedEntity
   CreatorId
   CreatorId
   LastModifierId
   IRoleDataScopeProvider
   CustomPermissionName
   CreationAuditedAggregateRoot
   LastModifierId
   LastModificationTime
   ExtensionFlags

## core/PubSoft.Events (4 uncovered)
   OccurredOn
   DomainEventServiceCollectionExtensions
   DomainEventHandlerServiceCollectionExtensions
   OccurredOn

## core/PubSoft.Identity (1 uncovered)
   PhoneNumberConfirmed

## core/PubSoft.Logging (9 uncovered)
   ServiceInstanceId
   LoggingServiceCollectionExtensions
   AppVersion
   MinLevel
   ConsoleLogSink
   ServiceVersion
   AsyncBackbone
   HostName
   SpanId

## core/PubSoft.Mapping (1 uncovered)
   MappingBuilder

## core/PubSoft.MultiTenancy (4 uncovered)
   ITenantAppService
   CreateTenantInput
   UpdateTenantInput
   TenantDto

## core/PubSoft.Resilience (3 uncovered)
   RetryOnTimeout
   RetryOn5xx
   RetryOnConnectionFailure

## data/PubSoft.Auditing (4 uncovered)
   SensitiveProperties
   TargetType
   IgnoredEntityTypes
   TargetName

## data/PubSoft.Auditing.Outbox (1 uncovered)
   AuditOutboxServiceCollectionExtensions

## data/PubSoft.EntityFrameworkCore (28 uncovered)
   P95Ms
   CurrentTenantId
   TenantModelBuilderExtensions
   AverageMs
   EfCoreServiceCollectionExtensions
   ErrorCount
   AuditPersistenceModelBuilderExtensions
   MaxMs
   SlowQueryInterceptor
   DbQueryDiagnosticsServiceCollectionExtensions
   SnowflakeModelBuilderExtensions
   CurrentMemberId
   DiagnosticSnapshot
   PermissionGrantModelBuilderExtensions
   EfCoreHealthCheckExtensions
   DatabaseMigrationServiceCollectionExtensions
   TargetName
   P50Ms
   QuerySnapshot
   P99Ms
   EfCoreServiceDefaultsExtensions
   AuditChangeCaptureHook
   DurationMilliseconds
   IdentityModelBuilderExtensions
   TargetType
   EfCoreQueryExtensions
   TotalMs
   AuditPersistenceServiceCollectionExtensions

## host/PubSoft.AspNetCore (43 uncovered)
   TenantContextServiceCollectionExtensions
   StrictTransportSecurity
   NotFoundWindowSeconds
   OAuthPathPrefixes
   PubSoftRateLimitingExtensions
   PubSoftForwardedHeadersOptions
   PermissionsPolicy
   ArgumentExceptionStatusCode
   ReferrerPolicy
   EnableHstsInDevelopment
   PubSoftApplicationService
   PubSoftSecurityExtensions
   PubSoftRateLimitPolicyExtensions
   LoginWindowSeconds
   ExcludedPathPrefixes
   CorrelationHeaderName
   NotFoundPermitLimit
   OAuthWindowSeconds
   SecurityHeadersMiddlewareExtensions
   PubSoftController
   PubSoftControllerBase
   ValidatePathPrefixes
   DefaultStatusCode
   LoginPathPrefixes
   XContentTypeOptions
   QueueLimit
   UnauthorizedAccessStatusCode
   PubSoftRateLimitRejectionExtensions
   HmacValidationServiceCollectionExtensions
   ValidatePermitLimit
   ... +13 more

## host/PubSoft.AspNetCore.Docs (8 uncovered)
   IncludeNonActionEndpoints
   ApiDocsExtensions
   DetectAuthMetadata
   GroupBy
   PagePath
   RequireAuthorization
   CatalogPath
   Summary

## host/PubSoft.Edge (21 uncovered)
   IEdgeSchemePolicy
   EdgeGatewayExtensions
   EdgeOpenApiExtensions
   Md5PermitLimit
   HmacPermitLimit
   UiPath
   IsVerified
   SwaggerUiCssUrl
   SessionPermitLimit
   EdgeSchemeAttribute
   SwaggerUiBundleUrl
   EdgeGatewayMiddleware
   DocumentPath
   TimestampToleranceSeconds
   IsExpired
   IEdgeSessionStore
   EdgeSessionExtensions
   EdgeRateLimitMiddleware
   EdgeRateLimitExtensions
   QueueLimit
   UiEnabled

## host/PubSoft.Edge.Client (1 uncovered)
   EdgeClientExtensions

## host/PubSoft.Edge.Core (2 uncovered)
   EdgeErrorCodes
   EdgeSignResult

## messaging/PubSoft.Messaging.Outbox (7 uncovered)
   ProcessedAt
   OutboxModelBuilderExtensions
   OutboxProcessor
   MaximumPayloadCharacters
   NextAttemptAt
   OutboxServiceCollectionExtensions
   BatchSize

## platform/PubSoft.Caching (3 uncovered)
   CacheStatistics
   CachingServiceCollectionExtensions
   SizeLimit

## platform/PubSoft.Caching.Redis (3 uncovered)
   MaxValueBytes
   RedisDistributedLockServiceCollectionExtensions
   CachingRedisServiceCollectionExtensions

## platform/PubSoft.Modularity (4 uncovered)
   ModuleCount
   ShutdownToken
   ModuleType
   ModuleTypes

## platform/PubSoft.Monitoring (1 uncovered)
   MonitoringServiceCollectionExtensions

## platform/PubSoft.Monitoring.Scheduling (8 uncovered)
   ToState
   MonitoringSchedulingServiceCollectionExtensions
   AlertSeverity
   AlertContext
   WasProbeUnreachable
   FromState
   Severity
   JobHealthState

## platform/PubSoft.Notifications (12 uncovered)
   TemplateCode
   TeamsWebhookOptions
   TeamsWebhookChannel
   WebhookUrl
   DingTalkWebhookChannel
   DingTalkWebhookOptions
   Severity
   NotificationChannelExtensions
   NotificationServiceCollectionExtensions
   WebhookUrl
   NotificationDispatchReport
   NotificationSeverity

## platform/PubSoft.Outbound (3 uncovered)
   IOutboundContractRegistry
   OutboundServiceCollectionExtensions
   OutboundContractOptions

## platform/PubSoft.Resilience.Http (1 uncovered)
   HttpResilienceServiceCollectionExtensions

## platform/PubSoft.Scheduling (6 uncovered)
   IResultProcessorRegistry
   SchedulerHostedService
   LoopInterval
   ErrorDelay
   SchedulerServiceCollectionExtensions
   LoggingResultProcessor

## platform/PubSoft.Scheduling.EntityFrameworkCore (2 uncovered)
   SchedulingEntityFrameworkCoreServiceCollectionExtensions
   SchedulerStateModelBuilderExtensions

## platform/PubSoft.Scheduling.Http (1 uncovered)
   HttpExecutorServiceCollectionExtensions

## platform/PubSoft.Tokens (4 uncovered)
   TokenServiceCollectionExtensions
   TokenRequestOptions
   DefaultHeaderName
   DefaultHeaderPrefix

## security/PubSoft.Authentication.Jwt (2 uncovered)
   JwtJwksDocument
   JwtJwksKey

## security/PubSoft.Authentication.Jwt.AspNetCore (10 uncovered)
   DualJwtOptions
   NameClaimType
   BackchannelTimeout
   ClockSkew
   JwtBearerAuthenticationExtensions
   DualJwtAuthenticationExtensions
   PubSoftJwtBearerOptions
   RequireHttpsMetadata
   CacheMinutes
   RoleClaimType

## security/PubSoft.Authentication.OAuthProviders (31 uncovered)
   AppleOAuthProvider
   NickName
   QQOAuthProvider
   HuaweiOAuthProvider
   DouyinOAuthProvider
   GoogleOAuthProvider
   AvatarUrl
   Gender
   ToutiaoOAuthProvider
   XiaomiOAuthProvider
   NickName
   GenericOAuthProvider
   OAuthAuthorizeUrlResult
   OAuthPhoneResult
   GiteeOAuthProvider
   OAuthProviderServiceCollectionExtensions
   City
   OAuthProviderBase
   PurePhoneNumber
   AvatarUrl
   AlipayOAuthException
   IsDefault
   GitLabOAuthProvider
   TaobaoOAuthProvider
   MicrosoftOAuthProvider
   CountryCode
   BaiduOAuthProvider
   Province
   WeiboOAuthProvider
   OAuthCodeExchangeResult
   ... +1 more

## security/PubSoft.Authorization (14 uncovered)
   PinDisplayNameResolver
   PermissionModuleAutoRegistrar
   EnforceDerived
   PinSortOrderResolver
   RouteModuleMap
   ExtensionFlags
   DiscoveredPermissionModule
   PermissionModuleDiscoveryExtensions
   CustomPermissionsExtractor
   RouteModuleMap
   PinAvailableFlagsResolver
   IPermissionModuleRegistrationSink
   PinDescriptionResolver
   AutoPermissionOptions

## security/PubSoft.Security (14 uncovered)
   IdCardMaskChar
   ActiveIdentifiers
   BankCardMaskChar
   PasswordVerificationResult
   RemainingRequests
   EmailMaskChar
   LimitByIpAddress
   GeneralMaskChar
   PhoneMaskChar
   HmacSignatureResult
   MaxAppIdLength
   ResetTime
   RateLimitStats
   TotalIdentifiers

## security/PubSoft.Security.Redis (2 uncovered)
   RedisNonceStoreServiceCollectionExtensions
   MaxScopeLength

## covered-but-wrong-layer (test lives in different test project)
  core/PubSoft.Contracts: 32
  core/PubSoft.Cryptography: 2
  core/PubSoft.Domain: 24
  core/PubSoft.Events: 1
  core/PubSoft.Logging: 5
  core/PubSoft.Resilience: 9
  data/PubSoft.Auditing: 1
  data/PubSoft.EntityFrameworkCore: 6
  host/PubSoft.AspNetCore: 13
  host/PubSoft.AspNetCore.Docs: 2
  host/PubSoft.Edge: 9
  host/PubSoft.Edge.Client: 3
  host/PubSoft.Edge.Core: 1
  messaging/PubSoft.Messaging: 2
  messaging/PubSoft.Messaging.Outbox: 20
  platform/PubSoft.Caching: 2
  platform/PubSoft.Caching.Redis: 1
  platform/PubSoft.Monitoring: 1
  platform/PubSoft.Monitoring.Scheduling: 1
  platform/PubSoft.Notifications: 2
  platform/PubSoft.Outbound: 14
  platform/PubSoft.Scheduling: 2
  platform/PubSoft.Scheduling.EntityFrameworkCore: 7
  platform/PubSoft.Tokens: 24
  security/PubSoft.Authentication: 6
  security/PubSoft.Authentication.Jwt.AspNetCore: 4
  security/PubSoft.Authentication.OAuthProviders: 5
  security/PubSoft.Authorization: 6
  security/PubSoft.Security: 28
  security/PubSoft.Security.Redis: 5
