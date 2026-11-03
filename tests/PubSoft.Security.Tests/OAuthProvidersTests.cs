using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.OAuth;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class OAuthProvidersTests
{
    [Fact]
    public void RegistryRoutesAllBuiltInProvidersCaseInsensitively()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOAuthChannelConfigProvider>(new FakeConfigProvider(null));
        services.AddPubSoftOAuthProviders();
        using var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<IOAuthProviderRegistry>();
        Assert.Equal(OAuthProviderNames.BuiltIn.Length, registry.GetAllProviders().Count);
        Assert.IsType<WeChatOAuthProvider>(registry.GetProvider("wechat"));
        Assert.IsType<AlipayOAuthProvider>(registry.GetProvider("ALIPAY"));
        Assert.Null(registry.GetProvider("NoSuchProvider"));
        Assert.True(OAuthProviderNames.IsKnown("github"));
    }

    [Fact]
    public async Task WeChatExchangeCodeMapsJscode2SessionResponse()
    {
        var http = new StubHandler(Json("""
            {"openid":"oabc","unionid":"u123","session_key":"sk"}
            """));
        var provider = new WeChatOAuthProvider(
            new FakeConfigProvider(new OAuthChannelConfig { AppId = "wx1", AppSecret = "s" }),
            new FakeAccessTokenProvider(), new HttpClient(http));

        var result = await provider.ExchangeCodeAsync("wx1", "code1", OAuthChannelTypes.MiniProgram);

        Assert.True(result.Success);
        Assert.Equal("oabc", result.OpenId);
        Assert.Equal("u123", result.UnionId);
        Assert.Equal("sk", result.SessionKey);
        Assert.Contains("jscode2session", http.LastRequestUri);
    }

    [Fact]
    public async Task WeChatExchangeCodeFailsOnErrcode()
    {
        var http = new StubHandler(Json("""{"errcode":40029,"errmsg":"invalid code"}"""));
        var provider = new WeChatOAuthProvider(
            new FakeConfigProvider(new OAuthChannelConfig { AppId = "wx1", AppSecret = "s" }),
            new FakeAccessTokenProvider(), new HttpClient(http));

        var result = await provider.ExchangeCodeAsync("wx1", "bad", OAuthChannelTypes.MiniProgram);

        Assert.False(result.Success);
        Assert.Equal("invalid code", result.Message);
    }

    [Fact]
    public async Task WeComExchangeCodeRunsGetTokenThenUserInfo()
    {
        var http = new StubHandler(
            Json("""{"errcode":0,"access_token":"corp-token","expires_in":7200}"""),
            Json("""{"errcode":0,"userid":"zhangsan","openid":"oX"}"""));
        var provider = new WeComOAuthProvider(
            new FakeConfigProvider(new OAuthChannelConfig { AppId = "corp1", AppSecret = "sec" }),
            new HttpClient(http));

        var result = await provider.ExchangeCodeAsync("corp1", "code1");

        Assert.True(result.Success);
        Assert.Equal("zhangsan", result.UserId);
        Assert.Equal(2, http.RequestCount);
        Assert.Contains("getuserinfo", http.LastRequestUri);
    }

    [Fact]
    public async Task DingTalkSignsRequestWithHmacSha256()
    {
        var http = new StubHandler(Json("""{"errcode":0,"user_info":{"openid":"doid","unionid":"duid","nick":"n"}}"""));
        var provider = new DingTalkOAuthProvider(
            new FakeConfigProvider(new OAuthChannelConfig { AppId = "ding1", AppSecret = "dingsec" }),
            new HttpClient(http));

        var result = await provider.ExchangeCodeAsync("ding1", "dtcode");

        Assert.True(result.Success);
        Assert.Equal("doid", result.OpenId);
        var uri = new Uri(http.LastRequestUri!);
        Assert.Equal("ding1", GetQuery(uri, "accessKey"));
        Assert.NotNull(GetQuery(uri, "timestamp"));
        Assert.NotNull(GetQuery(uri, "signature"));
        Assert.Contains("tmp_auth_code", http.LastRequestBody);
    }

    [Fact]
    public async Task GenericProviderFallsBackToUserInfoForNumericId()
    {
        // GitHub token responses carry no openid; the user id is a JSON number.
        var http = new StubHandler(
            Json("""{"access_token":"gh-tok","token_type":"bearer"}"""),
            Json("""{"id":12345,"login":"octocat","avatar_url":"https://a/b.png"}"""));
        var provider = new GitHubOAuthProvider(
            new FakeConfigProvider(new OAuthChannelConfig { AppId = "gh1", AppSecret = "sec" }),
            new HttpClient(http));

        var result = await provider.ExchangeCodeAsync("gh1", "code1");

        Assert.True(result.Success);
        Assert.Equal("12345", result.OpenId);
        Assert.Equal(2, http.RequestCount);
        Assert.Contains("api.github.com/user", http.LastRequestUri);
    }

    [Fact]
    public async Task ExchangeFailsClosedWhenChannelConfigOrSecretMissing()
    {
        var http = new StubHandler(Json("{}"));
        var provider = new GitHubOAuthProvider(
            new FakeConfigProvider(null), new HttpClient(http));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.ExchangeCodeAsync("missing", "code"));

        var noSecret = new GitHubOAuthProvider(
            new FakeConfigProvider(new OAuthChannelConfig { AppId = "x", AppSecret = null }),
            new HttpClient(http));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => noSecret.ExchangeCodeAsync("x", "code"));
        Assert.Equal(0, http.RequestCount);
    }

    [Fact]
    public async Task TwitterAuthorizeUrlRequiresPkceChallenge()
    {
        var provider = new TwitterOAuthProvider(
            new FakeConfigProvider(null), new HttpClient(new StubHandler(Json("{}"))));

        var missing = await provider.GetAuthorizeUrlAsync(new OAuthAuthorizeUrlRequest { AppId = "tw1" });
        Assert.False(missing.Success);

        var ok = await provider.GetAuthorizeUrlAsync(new OAuthAuthorizeUrlRequest
        {
            AppId = "tw1",
            RedirectUri = "https://app.test/cb",
            CodeChallenge = "abc123",
        });
        Assert.True(ok.Success);
        Assert.Contains("code_challenge=abc123", ok.AuthorizeUrl);
        Assert.Contains("code_challenge_method=S256", ok.AuthorizeUrl);
    }

    [Fact]
    public async Task AuthorizeUrlsPercentEncodeReservedCharacters()
    {
        var providers = new IOAuthProvider[]
        {
            new AlipayOAuthProvider(new FakeConfigProvider(null), new HttpClient(new StubHandler(Json("{}")))),
            new DingTalkOAuthProvider(new FakeConfigProvider(null), new HttpClient(new StubHandler(Json("{}")))),
            new GitHubOAuthProvider(new FakeConfigProvider(null), new HttpClient(new StubHandler(Json("{}")))),
            new WeChatOAuthProvider(new FakeConfigProvider(null), new FakeAccessTokenProvider(), new HttpClient(new StubHandler(Json("{}")))),
            new WeComOAuthProvider(new FakeConfigProvider(null), new HttpClient(new StubHandler(Json("{}")))),
            new TwitterOAuthProvider(new FakeConfigProvider(null), new HttpClient(new StubHandler(Json("{}")))),
        };

        foreach (var provider in providers)
        {
            var result = await provider.GetAuthorizeUrlAsync(new OAuthAuthorizeUrlRequest
            {
                AppId = "app&id#1",
                RedirectUri = "https://app.test/cb",
                State = "st&ate#x",
                Scope = "openid profile",
                CodeChallenge = "abc123",
            });
            Assert.True(result.Success, provider.GetType().Name);
            var queryIndex = result.AuthorizeUrl.IndexOf('?');
            var fragmentIndex = result.AuthorizeUrl.IndexOf('#');
            var query = fragmentIndex > queryIndex && fragmentIndex > 0
                ? result.AuthorizeUrl[(queryIndex + 1)..fragmentIndex]
                : result.AuthorizeUrl[(queryIndex + 1)..];
            Assert.DoesNotContain("&id#1", query);
            Assert.DoesNotContain("st&ate", query);
            var parsed = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(query);
            var appId = parsed.TryGetValue("app_id", out var v1) ? v1.ToString()
                : parsed.TryGetValue("appid", out var v2) ? v2.ToString()
                : parsed.TryGetValue("client_id", out var v3) ? v3.ToString() : "";
            Assert.Equal("app&id#1", appId);
            Assert.Equal("st&ate#x", parsed["state"].ToString());
        }
    }

    [Fact]
    public async Task AlipayExchangeSignsRequestAndVerifiesResponseSignature()
    {
        using var appKey = RSA.Create(2048);
        using var platformKey = RSA.Create(2048);
        var appPrivatePem = PemEncode("PRIVATE KEY", appKey.ExportPkcs8PrivateKey());
        var platformPublicPem = PemEncode("PUBLIC KEY", platformKey.ExportSubjectPublicKeyInfo());
        var platformPrivatePem = PemEncode("PRIVATE KEY", platformKey.ExportPkcs8PrivateKey());

        var http = new StubHandler(_ =>
        {
            const string inner = """{"access_token":"at","refresh_token":"rt","open_id":"aloid","user_id":"2088xx"}""";
            using var verifyKey = RSA.Create();
            verifyKey.ImportPkcs8PrivateKey(
                Convert.FromBase64String(platformPrivatePem
                    .Replace("-----BEGIN PRIVATE KEY-----", "")
                    .Replace("-----END PRIVATE KEY-----", "")
                    .Replace("\n", "").Trim()), out int _);
            var sign = Convert.ToBase64String(
                verifyKey.SignData(Encoding.UTF8.GetBytes(inner), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            return Task.FromResult(JsonResponse($$"""{"alipay_system_oauth_token_response":{{inner}},"sign":"{{sign}}"}"""));
        });

        var provider = new AlipayOAuthProvider(
            new FakeConfigProvider(new OAuthChannelConfig
            {
                AppId = "ali1",
                ExtraJson = JsonSerializer.Serialize(new { PrivateKeyPem = appPrivatePem, PlatformPublicKeyPem = platformPublicPem }),
            }),
            new HttpClient(http));

        var result = await provider.ExchangeCodeAsync("ali1", "authcode");

        Assert.True(result.Success);
        Assert.Equal("aloid", result.OpenId);
        Assert.Equal("2088xx", result.UserId);
        Assert.Equal("at", result.AccessToken);
        Assert.Contains("sign=", http.LastRequestBody);
        Assert.Contains("sign_type=RSA2", http.LastRequestBody);
    }

    [Fact]
    public async Task AlipayExchangeFailsWhenResponseSignatureInvalid()
    {
        using var appKey = RSA.Create(2048);
        using var platformKey = RSA.Create(2048);
        var appPrivatePem = PemEncode("PRIVATE KEY", appKey.ExportPkcs8PrivateKey());
        var platformPublicPem = PemEncode("PUBLIC KEY", platformKey.ExportSubjectPublicKeyInfo());

        var http = new StubHandler(Json("""{"alipay_system_oauth_token_response":{"open_id":"x","user_id":"y"},"sign":"bogus"}"""));
        var provider = new AlipayOAuthProvider(
            new FakeConfigProvider(new OAuthChannelConfig
            {
                AppId = "ali1",
                ExtraJson = JsonSerializer.Serialize(new { PrivateKeyPem = appPrivatePem, PlatformPublicKeyPem = platformPublicPem }),
            }),
            new HttpClient(http));

        var result = await provider.ExchangeCodeAsync("ali1", "authcode");

        Assert.False(result.Success);
        Assert.Contains("signature verification", result.Message);
    }

    private static string Json(string body) => body;

    private static HttpResponseMessage JsonResponse(string body)
        => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string? GetQuery(Uri uri, string name)
        => uri.Query.TrimStart('?').Split('&')
            .Select(p => p.Split('=')).FirstOrDefault(p => p[0] == name)?[1];

    private static string PemEncode(string label, byte[] der)
        => $"-----BEGIN {label}-----\n{Convert.ToBase64String(der, Base64FormattingOptions.InsertLineBreaks)}\n-----END {label}-----\n";

    private sealed class FakeConfigProvider(OAuthChannelConfig? config) : IOAuthChannelConfigProvider
    {
        public Task<OAuthChannelConfig?> GetByAppIdAsync(string appId, CancellationToken ct = default)
            => Task.FromResult(config);
        public Task<OAuthChannelConfig?> GetDefaultAsync(string provider, string channelType, CancellationToken ct = default)
            => Task.FromResult(config);
        public Task<List<OAuthChannelConfig>> ListByProviderAsync(string provider, CancellationToken ct = default)
            => Task.FromResult(config is null ? new List<OAuthChannelConfig>() : new List<OAuthChannelConfig> { config });
        public Task<List<OAuthChannelConfig>> ListAsync(CancellationToken ct = default)
            => Task.FromResult(config is null ? new List<OAuthChannelConfig>() : new List<OAuthChannelConfig> { config });
    }

    private sealed class FakeAccessTokenProvider : IWeChatAccessTokenProvider
    {
        public Task<WeChatAccessTokenResult> GetAsync(string appId, CancellationToken ct = default)
            => Task.FromResult(WeChatAccessTokenResult.Ok("fake-token"));
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _responses;
        private int _count;

        public StubHandler(params string[] bodies)
            => _responses = new(bodies.Select<string, Func<CancellationToken, Task<HttpResponseMessage>>>(
                body => _ => Task.FromResult(JsonResponse(body))));

        public StubHandler(Func<CancellationToken, Task<HttpResponseMessage>> responder)
            => _responses = new([responder]);

        public int RequestCount => _count;
        public string? LastRequestUri { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _count++;
            LastRequestUri = request.RequestUri?.ToString();
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var responder = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
            return await responder(cancellationToken);
        }
    }
}
