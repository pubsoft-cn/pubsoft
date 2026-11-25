using System.Text.Json;
using System.Text.Json.Serialization;

namespace PubSoft.Monitoring.Scheduling;

// Reflection-free metadata for the alert-rule job payload so the package stays Native AOT compatible.
// ScalarStringJsonConverter lets string-typed members accept numeric/boolean tokens that
// IConfiguration materialization inferred (e.g. a template parameter value of "8000").
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web,
    Converters = [typeof(JsonStringEnumConverter<AlertConditionType>), typeof(JsonStringEnumConverter<AlertSeverity>), typeof(PubSoft.Scheduling.ScalarStringJsonConverter)])]
[JsonSerializable(typeof(AlertRuleConfig))]
[JsonSerializable(typeof(AlertRule))]
internal sealed partial class AlertJsonContext : JsonSerializerContext;
