using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PubSoft.Authentication.Jwt;
using PubSoft.Authentication.Jwt.AspNetCore;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class JwtBearerAuthenticationTests
{
    [Fact]
    public void BearerValidationUsesExactRsaKeyAndPreservesJwtClaimNames()
    {
        using var signingKey = JwtSigning.CreateRsaKey("active-key");
        var bearerOptions = CreateBearerOptions(SerializeJwks(signingKey));
        var token = JwtSigning.CreateAccessToken(signingKey, "issuer", "api",
            new Dictionary<string, string> { [JwtRegisteredClaimNames.Sub] = "user-42" }, TimeSpan.FromMinutes(5));

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = bearerOptions.MapInboundClaims };
        var principal = handler.ValidateToken(
            token,
            bearerOptions.TokenValidationParameters,
            out _);

        Assert.False(bearerOptions.MapInboundClaims);
        Assert.Equal(SecurityAlgorithms.RsaSha256, Assert.Single(bearerOptions.TokenValidationParameters.ValidAlgorithms!));
        Assert.Equal("user-42", principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
    }

    [Fact]
    public void BearerValidationRejectsUnknownKeyIdWithoutFallback()
    {
        using var trustedKey = JwtSigning.CreateRsaKey("trusted-key");
        using var unknownKey = JwtSigning.CreateRsaKey("unknown-key");
        var bearerOptions = CreateBearerOptions(SerializeJwks(trustedKey));
        var token = JwtSigning.CreateAccessToken(unknownKey, "issuer", "api",
            new Dictionary<string, string> { [JwtRegisteredClaimNames.Sub] = "user-42" }, TimeSpan.FromMinutes(5));

        Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler().ValidateToken(
            token,
            bearerOptions.TokenValidationParameters,
            out _));
    }

    [Fact]
    public void BearerConfigurationRejectsPrivateJwkMaterial()
    {
        using var signingKey = JwtSigning.CreateRsaKey("private-key");
        var jwks = JsonNode.Parse(SerializeJwks(signingKey))!.AsObject();
        jwks["keys"]![0]!["d"] = "AQ";
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddAuthentication().AddPubSoftJwtBearer(options =>
        {
            options.Issuer = "issuer";
            options.Audience = "api";
            options.JwksJson = jwks.ToJsonString();
        }));
    }

    [Fact]
    public async Task RemoteMetadataLoadsSigningKeysAsynchronously()
    {
        using var signingKey = JwtSigning.CreateRsaKey("remote-key");
        var jwks = SerializeJwks(signingKey);
        var services = new ServiceCollection();
        services.AddAuthentication().AddPubSoftJwtBearer(options =>
        {
            options.Issuer = "https://issuer.example";
            options.Audience = "api";
            options.MetadataAddress = "https://issuer.example/.well-known/openid-configuration";
        });
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
            options => options.BackchannelHttpHandler = new MetadataHandler(jwks));

        using var serviceProvider = services.BuildServiceProvider();
        var bearer = serviceProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var configuration = await bearer.ConfigurationManager!.GetConfigurationAsync(CancellationToken.None);

        Assert.Equal("https://issuer.example", configuration.Issuer);
        Assert.Contains(configuration.SigningKeys, key => key.KeyId == "remote-key");
        Assert.True(bearer.RefreshOnIssuerKeyNotFound);
        Assert.False(bearer.TokenValidationParameters.TryAllIssuerSigningKeys);
        bearer.TokenValidationParameters.IssuerSigningKeys = configuration.SigningKeys;
        var resolver = bearer.TokenValidationParameters.IssuerSigningKeyResolver!;
        Assert.Equal("remote-key", Assert.Single(resolver("", null!, "remote-key", bearer.TokenValidationParameters)).KeyId);
        Assert.Empty(resolver("", null!, "unknown-key", bearer.TokenValidationParameters));
    }

    [Fact]
    public async Task RemoteMetadataCanRecoverAfterAnUnavailableIssuer()
    {
        using var signingKey = JwtSigning.CreateRsaKey("recovered-key");
        var services = new ServiceCollection();
        var backchannelHandler = new RecoveringMetadataHandler(SerializeJwks(signingKey));
        services.AddAuthentication().AddPubSoftJwtBearer(options =>
        {
            options.Issuer = "https://issuer.example";
            options.Audience = "api";
            options.MetadataAddress = "https://issuer.example/.well-known/openid-configuration";
        });
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
            options => options.BackchannelHttpHandler = backchannelHandler);

        using var serviceProvider = services.BuildServiceProvider();
        var bearer = serviceProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var configurationManager = bearer.ConfigurationManager!;

        var configuration = await configurationManager.GetConfigurationAsync(CancellationToken.None);

        Assert.Contains(configuration.SigningKeys, key => key.KeyId == "recovered-key");
        Assert.True(backchannelHandler.MetadataRequests >= 2);
    }

    [Fact]
    public async Task RemoteMetadataRetrievalHonorsCancellation()
    {
        var services = new ServiceCollection();
        services.AddAuthentication().AddPubSoftJwtBearer(options =>
        {
            options.Issuer = "https://issuer.example";
            options.Audience = "api";
            options.MetadataAddress = "https://issuer.example/.well-known/openid-configuration";
        });
        services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
            options => options.BackchannelHttpHandler = new CancellationMetadataHandler());

        using var serviceProvider = services.BuildServiceProvider();
        var bearer = serviceProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            bearer.ConfigurationManager!.GetConfigurationAsync(cancellationSource.Token));
    }

    [Fact]
    public void RemoteMetadataRequiresHttpsByDefaultAndSourceMustBeUnambiguous()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddAuthentication().AddPubSoftJwtBearer(options =>
        {
            options.Issuer = "issuer";
            options.Audience = "api";
            options.MetadataAddress = "http://issuer.example/.well-known/openid-configuration";
        }));
        Assert.Throws<ArgumentException>(() => services.AddAuthentication().AddPubSoftJwtBearer(options =>
        {
            options.Issuer = "issuer";
            options.Audience = "api";
            options.MetadataAddress = "https://issuer.example/.well-known/openid-configuration";
            options.JwksJson = "{}";
        }));
    }


    [Fact]
    public async Task SymmetricModeValidatesHs256TokenIssuedByMatchingKey()
    {
        using var key = JwtSigning.CreateSymmetricKey("sym-1");
        var services = new ServiceCollection();
        services.AddAuthentication().AddPubSoftJwtBearer(options =>
        {
            options.Issuer = "issuer";
            options.Audience = "api";
            options.SymmetricKey = ((Microsoft.IdentityModel.Tokens.SymmetricSecurityKey)key.SecurityKey).Key;
            options.SymmetricKeyId = "sym-1";
        });

        using var serviceProvider = services.BuildServiceProvider();
        var bearer = serviceProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
        var parameters = bearer.TokenValidationParameters;

        Assert.Equal([SecurityAlgorithms.HmacSha256], parameters.ValidAlgorithms);

        var token = JwtSigning.CreateAccessToken(key, "issuer", "api",
            new Dictionary<string, string> { ["sub"] = "42" }, TimeSpan.FromMinutes(5));
        var handler = new JwtSecurityTokenHandler();
        var result = await handler.ValidateTokenAsync(token, parameters);

        Assert.True(result.IsValid);
        Assert.Contains(result.ClaimsIdentity.Claims, c => c.Value == "42");
    }

    [Fact]
    public async Task SymmetricModeRejectsWrongKeyAndCrossAlgorithm()
    {
        using var good = JwtSigning.CreateSymmetricKey("sym-1");
        using var other = JwtSigning.CreateSymmetricKey("sym-2");
        var services = new ServiceCollection();
        services.AddAuthentication().AddPubSoftJwtBearer(options =>
        {
            options.Issuer = "issuer";
            options.Audience = "api";
            options.SymmetricKey = ((Microsoft.IdentityModel.Tokens.SymmetricSecurityKey)good.SecurityKey).Key;
            options.SymmetricKeyId = "sym-1";
        });

        using var serviceProvider = services.BuildServiceProvider();
        var parameters = serviceProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;
        var handler = new JwtSecurityTokenHandler();

        var wrongKeyToken = JwtSigning.CreateAccessToken(other, "issuer", "api",
            new Dictionary<string, string> { ["sub"] = "1" }, TimeSpan.FromMinutes(5));
        var wrongKey = await handler.ValidateTokenAsync(wrongKeyToken, parameters);
        Assert.False(wrongKey.IsValid);

        using var rsa = JwtSigning.CreateRsaKey("rsa-1");
        var rs256Token = JwtSigning.CreateAccessToken(rsa, "issuer", "api",
            new Dictionary<string, string> { ["sub"] = "1" }, TimeSpan.FromMinutes(5));
        var crossAlg = await handler.ValidateTokenAsync(rs256Token, parameters);
        Assert.False(crossAlg.IsValid);
    }

    [Fact]
    public void SymmetricModeRejectsWeakKeyAndConflictingModes()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddAuthentication().AddPubSoftJwtBearer(options =>
        {
            options.Issuer = "issuer";
            options.Audience = "api";
            options.SymmetricKey = new byte[16];   // 128 bits < 256 required
        }));
        Assert.Throws<ArgumentException>(() => services.AddAuthentication().AddPubSoftJwtBearer(options =>
        {
            options.Issuer = "issuer";
            options.Audience = "api";
            options.SymmetricKey = new byte[32];
            options.JwksJson = "{}";
        }));
    }

    private static JwtBearerOptions CreateBearerOptions(string jwksJson)
    {
        var services = new ServiceCollection();
        services.AddAuthentication().AddPubSoftJwtBearer(options =>
        {
            options.Issuer = "issuer";
            options.Audience = "api";
            options.JwksJson = jwksJson;
        });

        using var serviceProvider = services.BuildServiceProvider();
        return serviceProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
    }

    private static string SerializeJwks(RsaSigningKeyMaterial key) =>
        JsonSerializer.Serialize(JwtSigning.ExportJwks(key), JsonSerializerOptions.Web);

    private sealed class MetadataHandler(string jwks) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (content, mediaType) = request.RequestUri!.AbsolutePath.EndsWith("openid-configuration", StringComparison.Ordinal)
                ? ("{\"issuer\":\"https://issuer.example\",\"jwks_uri\":\"https://issuer.example/keys\"}", "application/json")
                : (jwks, "application/json");
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, mediaType)
            });
        }
    }

    private sealed class RecoveringMetadataHandler(string jwks) : HttpMessageHandler
    {
        private int _metadataRequests;

        public int MetadataRequests => Volatile.Read(ref _metadataRequests);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("openid-configuration", StringComparison.Ordinal)
                && Interlocked.Increment(ref _metadataRequests) == 1)
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));

            var (content, mediaType) = request.RequestUri.AbsolutePath.EndsWith("openid-configuration", StringComparison.Ordinal)
                ? ("{\"issuer\":\"https://issuer.example\",\"jwks_uri\":\"https://issuer.example/keys\"}", "application/json")
                : (jwks, "application/json");
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(content, System.Text.Encoding.UTF8, mediaType)
            });
        }
    }

    private sealed class CancellationMetadataHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromCanceled<HttpResponseMessage>(cancellationToken);
    }
}