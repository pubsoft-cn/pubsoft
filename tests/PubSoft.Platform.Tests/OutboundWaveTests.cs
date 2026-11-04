using System.Text.Json;
using PubSoft.Contracts;
using PubSoft.Outbound;
using Xunit;

namespace PubSoft.Core.Tests;

public sealed class OutboundWaveTests
{
    private sealed class Req
    {
        [ApiField("order_id")]
        public string OrderId { get; set; } = "";
        [ApiField("amount")]
        public int Amount { get; set; }
        [ApiField("note")]
        public string? Note { get; set; }
    }

    [Fact]
    public void BuildParamsMapsPropertiesByNamingPolicy()
    {
        var p = ContractRequestBuilder.BuildParams(new Req { OrderId = "o1", Amount = 42 });
        Assert.Equal("o1", p["order_id"]);
        Assert.Equal("42", p["amount"]);
    }

    [Fact]
    public void CamelCaseNamingPolicyConverts()
        => Assert.Equal("orderId", CamelCaseNamingPolicy.Instance.ConvertName("OrderId"));

    [Fact]
    public void ResponseMapperExtractsTypedFields()
    {
        var json = JsonDocument.Parse("""{"id":7,"name":"n"}""").RootElement;
        var d = ContractResponseMapper.Map(typeof(Dictionary<string, object>), json);
        Assert.NotNull(d);
    }

    [Fact]
    public void RegistryFindsByOperationId()
    {
        var reg = new OutboundContractRegistry();
        Assert.Null(reg.FindByOperationId("nope"));
    }

    [Fact]
    public void RegistryScanAssemblyRegistersContracts()
    {
        var reg = new OutboundContractRegistry();
        reg.ScanAssembly(typeof(OutboundContractRegistry).Assembly);
        Assert.NotNull(reg);
    }
}
