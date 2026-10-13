using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PubSoft.Authentication.Jwt;
using PubSoft.Authentication.Jwt.AspNetCore;

using var key = JwtSigning.CreateRsaKey("consumer-key");
var jwks = JsonSerializer.Serialize(JwtSigning.ExportJwks(key), JsonSerializerOptions.Web);
var services = new ServiceCollection();
services.AddAuthentication().AddPubSoftJwtBearer(options =>
{
    options.Issuer = "consumer-issuer";
    options.Audience = "consumer-api";
    options.JwksJson = jwks;
});

using var provider = services.BuildServiceProvider();
var bearer = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
    .Get(JwtBearerDefaults.AuthenticationScheme);
var token = JwtSigning.CreateAccessToken(key, "consumer-issuer", "consumer-api",
    new Dictionary<string, string> { [JwtRegisteredClaimNames.Sub] = "consumer-user" }, TimeSpan.FromMinutes(5));
var handler = new JwtSecurityTokenHandler { MapInboundClaims = bearer.MapInboundClaims };
var principal = handler.ValidateToken(token, bearer.TokenValidationParameters, out _);
if (principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value != "consumer-user"
    || bearer.MapInboundClaims
    || !bearer.TokenValidationParameters.ValidAlgorithms!.Contains(SecurityAlgorithms.RsaSha256))
    throw new InvalidOperationException("JWT bearer consumer validation failed.");

var remoteServices = new ServiceCollection();
remoteServices.AddAuthentication().AddPubSoftJwtBearer(options =>
{
    options.Issuer = "consumer-issuer";
    options.Audience = "consumer-api";
    options.MetadataAddress = "https://issuer.example/.well-known/openid-configuration";
});
using var metadataHandler = new ConsumerMetadataHandler(jwks);
remoteServices.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme,
    options => options.BackchannelHttpHandler = metadataHandler);
using var remoteProvider = remoteServices.BuildServiceProvider();
var remoteBearer = remoteProvider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
    .Get(JwtBearerDefaults.AuthenticationScheme);
var configuration = await remoteBearer.ConfigurationManager!.GetConfigurationAsync(CancellationToken.None);
if (configuration.Issuer != "consumer-issuer" || !configuration.SigningKeys.Any(key => key.KeyId == "consumer-key"))
    throw new InvalidOperationException("Asynchronous JWT metadata/JWKS consumer check failed.");

Console.WriteLine("Isolated PubSoft.Authentication.Jwt.AspNetCore consumer smoke check passed.");

sealed class ConsumerMetadataHandler(string jwks) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var content = request.RequestUri!.AbsolutePath.EndsWith("openid-configuration", StringComparison.Ordinal)
            ? "{\"issuer\":\"consumer-issuer\",\"jwks_uri\":\"https://issuer.example/keys\"}"
            : jwks;
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json")
        });
    }
}