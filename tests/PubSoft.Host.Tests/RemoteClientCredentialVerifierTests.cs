using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.AspNetCore.Authentication;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class RemoteClientCredentialVerifierTests
{
    [Fact]
    public async Task SuccessMapsClientIdTenantAndClaims()
    {
        HttpRequestMessage? captured = null;
        var verifier = Verifier(request =>
        {
            captured = request;
            return Json(HttpStatusCode.OK,
                new { success = true, clientId = "svc-billing", tenantId = 42, claims = new { scope = "read" } });
        });

        var result = await verifier.VerifyAsync("svc-billing", "secret-1");

        Assert.True(result.Success);
        Assert.Equal("svc-billing", result.ClientId);
        Assert.Equal(42L, result.TenantId);
        Assert.Equal("read", result.Claims?["scope"]);

        Assert.NotNull(captured);
        var body = await captured!.Content!.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.Equal("svc-billing", document.RootElement.GetProperty("clientId").GetString());
        Assert.Equal("secret-1", document.RootElement.GetProperty("clientSecret").GetString());
        Assert.Equal("https://registry.example/validate-secret", captured.RequestUri!.ToString());
    }

    [Fact]
    public async Task ConfigureRequestIsAppliedBeforeSend()
    {
        HttpRequestMessage? captured = null;
        var options = DefaultOptions();
        options.ConfigureRequest = request =>
        {
            request.Headers.TryAddWithoutValidation("X-Client-Id", "svc-caller");
            request.Headers.TryAddWithoutValidation("X-Client-Secret", "caller-secret");
        };
        var verifier = Verifier(request =>
        {
            captured = request;
            return Json(HttpStatusCode.OK, new { success = true, clientId = "svc-billing" });
        }, options);

        var result = await verifier.VerifyAsync("svc-billing", "secret-1");

        Assert.True(result.Success);
        Assert.Equal("svc-caller", captured!.Headers.GetValues("X-Client-Id").Single());
        Assert.Equal("caller-secret", captured.Headers.GetValues("X-Client-Secret").Single());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task NonSuccessStatusFailsClosed(HttpStatusCode status)
    {
        var verifier = Verifier(_ => new HttpResponseMessage(status));
        var result = await verifier.VerifyAsync("c", "s");
        Assert.False(result.Success);
    }

    [Fact]
    public async Task MalformedOrDeclinedPayloadsFailClosed()
    {
        var declined = Verifier(_ => Json(HttpStatusCode.OK, new { success = false }));
        Assert.False((await declined.VerifyAsync("c", "s")).Success);

        var missingClientId = Verifier(_ => Json(HttpStatusCode.OK, new { success = true }));
        Assert.False((await missingClientId.VerifyAsync("c", "s")).Success);

        var garbage = Verifier(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json", Encoding.UTF8, "application/json")
        });
        Assert.False((await garbage.VerifyAsync("c", "s")).Success);
    }

    [Fact]
    public async Task TransportErrorsFailClosed()
    {
        var verifier = Verifier(_ => throw new HttpRequestException("connection refused"));
        Assert.False((await verifier.VerifyAsync("c", "s")).Success);
    }

    [Fact]
    public async Task EnabledCacheDeduplicatesBothOutcomes()
    {
        var calls = 0;
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var options = DefaultOptions();
        options.CacheMinutes = 5;

        var failing = Verifier(_ => { calls++; return new HttpResponseMessage(HttpStatusCode.Unauthorized); }, options, cache);
        await failing.VerifyAsync("c", "bad");
        await failing.VerifyAsync("c", "bad");
        Assert.Equal(1, calls);

        var succeeding = Verifier(_ => { calls++; return Json(HttpStatusCode.OK, new { success = true, clientId = "c" }); }, options, cache);
        await succeeding.VerifyAsync("c", "good");
        await succeeding.VerifyAsync("c", "good");
        Assert.Equal(2, calls);
    }

    [Fact]
    public void OptionsValidateFailClosed()
    {
        Assert.Throws<ArgumentNullException>(() => Verifier(_ => throw new NotImplementedException(),
            new RemoteClientCredentialOptions()));

        Assert.Throws<ArgumentException>(() => Verifier(_ => throw new NotImplementedException(),
            new RemoteClientCredentialOptions { Endpoint = new Uri("http://registry.example/v") }));

        // Loopback plaintext is allowed for development.
        _ = Verifier(_ => throw new NotImplementedException(),
            new RemoteClientCredentialOptions { Endpoint = new Uri("http://localhost:8080/v") });

        Assert.Throws<ArgumentOutOfRangeException>(() => Verifier(_ => throw new NotImplementedException(),
            new RemoteClientCredentialOptions { Endpoint = new Uri("https://r.example/v"), Timeout = TimeSpan.FromSeconds(16) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Verifier(_ => throw new NotImplementedException(),
            new RemoteClientCredentialOptions { Endpoint = new Uri("https://r.example/v"), CacheMinutes = 61 }));
    }

    [Fact]
    public void RegistrationWiresVerifierAndValidatesAtRegistration()
    {
        var services = new ServiceCollection();
        services.AddPubSoftRemoteClientCredentialVerifier(options =>
            options.Endpoint = new Uri("https://registry.example/validate-secret"));
        using var provider = services.BuildServiceProvider();
        Assert.IsType<RemoteClientCredentialVerifier>(provider.GetRequiredService<IClientCredentialVerifier>());

        Assert.Throws<ArgumentNullException>(() => new ServiceCollection()
            .AddPubSoftRemoteClientCredentialVerifier(_ => { }));
    }

    private static RemoteClientCredentialVerifier Verifier(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        RemoteClientCredentialOptions? options = null,
        IMemoryCache? cache = null)
        => new(new HttpClient(new StubHandler(respond)), options ?? DefaultOptions(), cache);

    private static RemoteClientCredentialOptions DefaultOptions() => new()
    {
        Endpoint = new Uri("https://registry.example/validate-secret")
    };

    private static HttpResponseMessage Json(HttpStatusCode status, object payload)
        => new(status) { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request));
    }
}
