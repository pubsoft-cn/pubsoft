using System.Reflection;
using PubSoft.Contracts;

var request = new RecordRequest { RecordId = " 42 " };
((IContractNormalizer)request).Normalize();
if (request.RecordId != "42" || !((IContractValidator)request).Validate())
    throw new InvalidOperationException("Contract normalize/validate consumer check failed.");

var operation = typeof(RecordRequest).GetCustomAttribute<ApiOperationAttribute>();
var field = typeof(RecordRequest).GetProperty(nameof(RecordRequest.RecordId))!
    .GetCustomAttribute<ApiFieldAttribute>();
if (operation?.OperationId != "records.read" || operation.Aliases.Single() != "records.get"
    || operation.Verb != ApiHttpVerb.Get || field?.Name != "record_id"
    || field.Source != FieldSource.Query || !field.IsRequired)
    throw new InvalidOperationException("Contract metadata consumer check failed.");

if (CamelCaseNamingPolicy.Instance.ConvertName("RecordId") != "recordId"
    || SnakeCaseNamingPolicy.Instance.ConvertName("RecordId") != "record_id"
    || PascalCaseNamingPolicy.Instance.ConvertName("RecordId") != "RecordId")
    throw new InvalidOperationException("Contract naming policy consumer check failed.");

if (Guard.Required("name", " ") is null || Guard.Positive("amount", 0) is null
    || Guard.RequiredAmount("amount", "0") is null
    || !Guard.TryValidate(out var error, null, string.Empty) || error is not null)
    throw new InvalidOperationException("Contract validation guard consumer check failed.");

Console.WriteLine("Isolated PubSoft.Contracts consumer smoke check passed.");

[ApiOperation("records.read", Aliases = new[] { "records.get" }, Verb = ApiHttpVerb.Get)]
sealed class RecordRequest : IApiRequest<RecordResponse>, IContractNormalizer, IContractValidator
{
    [ApiField("record_id", IsRequired = true, Source = FieldSource.Query)]
    public string? RecordId { get; set; }

    public void Normalize() => RecordId = RecordId?.Trim();

    public bool Validate(out string? error)
    {
        error = Guard.Required("record_id", RecordId);
        return error is null;
    }
}

sealed class RecordResponse;