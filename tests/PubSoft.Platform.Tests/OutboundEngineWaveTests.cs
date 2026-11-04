using PubSoft.Contracts;
using PubSoft.Outbound;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class OutboundEngineWaveTests
{
    public sealed class Resp { public int Code { get; set; } }

    [ApiOperation("op.get")]
    public sealed class Req : IApiRequest<Resp>
    {
        [ApiField("id")] public int Id { get; set; }
    }

    private sealed class Provider(string name, string op, object result) : IOutboundProvider
    {
        public string ProviderName => name;
        public int Calls;
        public bool CanExecute(string operationId) => operationId == op;
        public Task<object> ExecuteAsync(string operationId, object request, OutboundProviderContext context, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private static OutboundEngine Engine(params IOutboundProvider[] ps)
        => new(ps, new OutboundContractRegistry());

    [Fact]
    public async Task ExecuteAsyncRoutesToMatchingProvider()
    {
        var p = new Provider("a", "op.get", new Resp { Code = 7 });
        var engine = Engine(p);
        var r = await engine.ExecuteAsync<Req, Resp>(new Req { Id = 1 }, "profile", 1L);
        Assert.Equal(7, r.Code);
        Assert.Equal(1, p.Calls);
    }

    [Fact]
    public async Task ExecuteAsyncThrowsWhenNoProvider()
    {
        var engine = Engine(new Provider("a", "op.other", new Resp()));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.ExecuteAsync<Req, Resp>(new Req(), "profile", 1L));
    }

    [Fact]
    public async Task ExecuteAsyncThrowsWhenAmbiguousProviders()
    {
        var engine = Engine(
            new Provider("a", "op.get", new Resp()),
            new Provider("b", "op.get", new Resp()));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.ExecuteAsync<Req, Resp>(new Req(), "profile", 1L));
        Assert.Contains("More than one", ex.Message);
    }

    [Fact]
    public async Task ExecuteAsyncNormalizesAndValidates()
    {
        var p = new Provider("a", "op.norm", new Resp());
        var engine = Engine(p);
        var req = new NormReq { Value = "  hi  " };
        await engine.ExecuteAsync<NormReq, Resp>(req, "profile", 1L);
        Assert.Equal("hi", req.Value);      // Normalize trimmed
        Assert.True(req.Normalized);
    }

    [ApiOperation("op.norm")]
    public sealed class NormReq : IApiRequest<Resp>, IContractNormalizer
    {
        public bool Normalized;
        [ApiField("v")] public string? Value { get; set; }
        public void Normalize() { Normalized = true; Value = Value?.Trim(); }
    }
}
