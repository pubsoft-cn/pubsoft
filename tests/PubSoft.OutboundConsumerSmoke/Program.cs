using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PubSoft.Contracts;
using PubSoft.Outbound;

var registry = new OutboundContractRegistry();
registry.ScanAssembly(Assembly.GetExecutingAssembly());
var metadata = registry.FindByOperationId("records.get");
if (metadata?.RequestType != typeof(ReadRecordRequest)
    || metadata.ResponseType != typeof(ReadRecordResponse)
    || OutboundContractMetadataResolver.RoutePath(metadata, "profile one") != "/outbound/v1/records/profile%20one/read")
    throw new InvalidOperationException("Outbound metadata/alias route check failed.");
try
{
    OutboundContractMetadataResolver.Resolve(typeof(InvalidOperationRequest));
    throw new InvalidOperationException("Invalid outbound operation path was accepted.");
}
catch (InvalidOperationException exception) when (exception.Message.Contains("Invalid operation ID", StringComparison.Ordinal))
{
}
try
{
    ((IList<ApiContractMetadata>)registry.Contracts).Add(metadata);
    throw new InvalidOperationException("Outbound contract registry exposed a mutable collection.");
}
catch (NotSupportedException)
{
}
var lateScanRegistry = new OutboundContractRegistry();
lateScanRegistry.GetMetadata(typeof(ReadRecordRequest));
lateScanRegistry.ScanAssembly(Assembly.GetExecutingAssembly());
if (lateScanRegistry.FindByOperationId("records.get")?.RequestType != typeof(ReadRecordRequest))
    throw new InvalidOperationException("Outbound metadata-before-scan alias registration check failed.");

var request = new ReadRecordRequest
{
    RecordId = " 42 ",
    Page = 0,
    Enabled = false,
    Secret = "not-for-query",
    Address = new RequestAddress { ZipCode = "101" }
};
var parameters = ContractRequestBuilder.BuildParams(request);
if (parameters["record_id"] != " 42 " || parameters["enabled"] != "false"
    || parameters["zip_code"] != "101" || parameters.ContainsKey("secret") || parameters.ContainsKey("page"))
    throw new InvalidOperationException("Outbound request field mapping check failed.");
if (RequiredFieldValidator.Validate(new ReadRecordRequest()) is null)
    throw new InvalidOperationException("Outbound required field check failed.");
if (RequiredFieldValidator.Validate(new NullableRequiredField { Count = 0 }) is null)
    throw new InvalidOperationException("Outbound nullable default required field check failed.");
try
{
    ContractRequestBuilder.BuildParams(new DuplicateWireNames());
    throw new InvalidOperationException("Outbound duplicate wire names were accepted.");
}
catch (InvalidOperationException exception) when (exception.Message.Contains("wire name", StringComparison.Ordinal))
{
}
var cycle = new FlattenCycle();
cycle.Next = cycle;
try
{
    ContractRequestBuilder.BuildParams(cycle);
    throw new InvalidOperationException("Outbound flatten cycle was accepted.");
}
catch (InvalidOperationException exception) when (exception.Message.Contains("cycle", StringComparison.Ordinal))
{
}

using var responseDocument = JsonDocument.Parse("""
    {"record_id":42,"amount":"12.50","enabled":true,"zip_code":"101","tags":["a","b"],"profile":{"display_name":"Ada"}}
    """);
var response = ContractResponseMapper.Map<ReadRecordResponse>(responseDocument.RootElement);
if (response.RecordId != "42" || response.Amount != 12.50m || !response.Enabled
    || response.Address?.ZipCode != "101" || !response.Tags.SequenceEqual(["a", "b"])
    || response.Profile?.DisplayName != "Ada")
    throw new InvalidOperationException("Outbound JSON response mapping check failed.");

using var contractDocument = JsonDocument.Parse("""{"record_id":"55","zip_code":"202"}""");
var deserialized = (ReadRecordRequest)ContractJsonDeserializer.Deserialize(
    contractDocument, typeof(ReadRecordRequest), SnakeCaseNamingPolicy.Instance);
if (deserialized.RecordId != "55" || deserialized.Address?.ZipCode != "202")
    throw new InvalidOperationException("Outbound contract JSON deserializer check failed.");

var dictionaryResponse = DictionaryResponseMapper.Map<ReadRecordResponse>(
    new Dictionary<string, string> { ["record_id"] = "77", ["amount"] = "3.25", ["zip_code"] = "303" },
    SnakeCaseNamingPolicy.Instance);
if (dictionaryResponse.RecordId != "77" || dictionaryResponse.Amount != 3.25m
    || dictionaryResponse.Address?.ZipCode != "303")
    throw new InvalidOperationException("Outbound dictionary response mapping check failed.");

var root = JsonValueConverter.ParseRoot("""{"amount":1234}""");
if (root is null || JsonValueConverter.GetAmountFen(root.Value, "amount") != 1234)
    throw new InvalidOperationException("Outbound JSON value conversion check failed.");

var provider = new RecordProvider();
var engine = new OutboundEngine([provider], registry);
request.Normalize();
var executed = await engine.ExecuteAsync<ReadRecordRequest, ReadRecordResponse>(request, "profile-a", 42);
if (executed.RecordId != "42" || provider.LastContext?.TenantId != 42
    || provider.LastContext.ProfileId != "profile-a" || provider.LastContext.OperationId != "records.read")
    throw new InvalidOperationException("Outbound execution context/dispatch check failed.");
try
{
    await new OutboundEngine([provider, new RecordProvider()], registry)
        .ExecuteAsync<ReadRecordRequest, ReadRecordResponse>(request, "profile-a", 42);
    throw new InvalidOperationException("Ambiguous outbound provider selection was accepted.");
}
catch (InvalidOperationException exception) when (exception.Message.Contains("More than one", StringComparison.Ordinal))
{
}
try
{
    await new OutboundEngine([new NormalizeProvider()], registry)
        .ExecuteAsync<NormalizeRequiredRequest, ReadRecordResponse>(
            new NormalizeRequiredRequest { Value = "present" }, "profile-a", 42);
    throw new InvalidOperationException("A required field cleared by normalization reached the provider.");
}
catch (InvalidOperationException exception) when (exception.Message.Contains("required", StringComparison.Ordinal))
{
}

var services = new ServiceCollection();
services.AddOutboundContracts(options => options.Assemblies.Add(Assembly.GetExecutingAssembly()));
services.AddExternalOutboundSdk();
if (!services.Any(descriptor => descriptor.ServiceType == typeof(IOutboundEngine))
    || !services.Any(descriptor => descriptor.ServiceType == typeof(IOutboundSdk)))
    throw new InvalidOperationException("Outbound DI registration check failed.");

Console.WriteLine("Isolated PubSoft.Outbound consumer smoke check passed.");

[ApiOperation("records.read", Aliases = new[] { "records.get" }, Verb = ApiHttpVerb.Get)]
public sealed class ReadRecordRequest : IApiRequest<ReadRecordResponse>, IContractNormalizer, IContractValidator
{
    [ApiField("record_id", IsRequired = true, Source = FieldSource.Query, Order = 1)]
    public string? RecordId { get; set; }

    [ApiField("page", Emit = EmitBehavior.IgnoreIfDefault, Order = 2)]
    public int Page { get; set; }

    [ApiField("enabled", Order = 3)]
    public bool Enabled { get; set; }

    [ApiField("secret", Source = FieldSource.Header)]
    public string? Secret { get; set; }

    [ApiField(Flatten = true, Order = 5)]
    public RequestAddress? Address { get; set; }

    public void Normalize() => RecordId = RecordId?.Trim();

    public bool Validate(out string? error)
    {
        error = Guard.Required("record_id", RecordId);
        return error is null;
    }
}

public sealed class ReadRecordResponse
{
    [ApiField("record_id")]
    public string? RecordId { get; set; }

    [ApiField("amount")]
    public decimal Amount { get; set; }

    [ApiField("enabled")]
    public bool Enabled { get; set; }

    [ApiField("tags")]
    public List<string> Tags { get; set; } = [];

    [ApiField(Flatten = true)]
    public ResponseAddress? Address { get; set; }

    [ApiField("profile")]
    public ReadProfile? Profile { get; set; }
}

public sealed class RequestAddress
{
    [ApiField("zip_code")]
    public string? ZipCode { get; set; }
}

public sealed class ResponseAddress
{
    [ApiField("zip_code")]
    public string? ZipCode { get; set; }
}

public sealed class ReadProfile
{
    [ApiField("display_name")]
    public string? DisplayName { get; set; }
}

public sealed class DuplicateWireNames
{
    [ApiField("same")]
    public string First { get; set; } = "a";

    [ApiField("same")]
    public string Second { get; set; } = "b";
}

public sealed class FlattenCycle
{
    [ApiField(Flatten = true)]
    public FlattenCycle? Next { get; set; }
}

public sealed class NullableRequiredField
{
    [ApiField("count", IsRequired = true)]
    public int? Count { get; set; }
}

public sealed class RecordProvider : IOutboundProvider
{
    public string ProviderName => "record-provider";
    public OutboundProviderContext? LastContext { get; private set; }
    public bool CanExecute(string operationId) => operationId == "records.read";

    public Task<object> ExecuteAsync(
        string operationId,
        object request,
        OutboundProviderContext context,
        CancellationToken cancellationToken = default)
    {
        LastContext = context;
        return Task.FromResult<object>(new ReadRecordResponse { RecordId = ((ReadRecordRequest)request).RecordId });
    }
}

[ApiOperation("records.normalize")]
public sealed class NormalizeRequiredRequest : IApiRequest<ReadRecordResponse>, IContractNormalizer
{
    [ApiField("value", IsRequired = true)]
    public string? Value { get; set; }

    public void Normalize() => Value = null;
}

public sealed class NormalizeProvider : IOutboundProvider
{
    public string ProviderName => "normalize-provider";
    public bool CanExecute(string operationId) => operationId == "records.normalize";

    public Task<object> ExecuteAsync(
        string operationId,
        object request,
        OutboundProviderContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult<object>(new ReadRecordResponse());
}

[ApiOperation("records/bad")]
internal sealed class InvalidOperationRequest : IApiRequest<ReadRecordResponse>;