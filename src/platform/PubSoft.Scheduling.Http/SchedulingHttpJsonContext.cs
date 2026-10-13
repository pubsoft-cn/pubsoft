using System.Text.Json.Serialization;

namespace PubSoft.Scheduling.Http;

// Reflection-free metadata for executor configuration so the package stays Native AOT compatible.
[JsonSerializable(typeof(HttpExecutorConfig))]
internal sealed partial class SchedulingHttpJsonContext : JsonSerializerContext;
