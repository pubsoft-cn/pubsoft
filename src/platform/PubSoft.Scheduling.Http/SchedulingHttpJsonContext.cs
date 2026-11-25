using System.Text.Json.Serialization;

namespace PubSoft.Scheduling.Http;

// Reflection-free metadata for executor configuration so the package stays Native AOT compatible.
// ScalarStringJsonConverter lets string-typed members (e.g. header values) accept numeric/boolean
// tokens that IConfiguration materialization inferred.
[JsonSourceGenerationOptions(Converters = [typeof(PubSoft.Scheduling.ScalarStringJsonConverter)])]
[JsonSerializable(typeof(HttpExecutorConfig))]
internal sealed partial class SchedulingHttpJsonContext : JsonSerializerContext;
