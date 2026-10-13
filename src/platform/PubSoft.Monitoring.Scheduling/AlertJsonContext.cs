using System.Text.Json;
using System.Text.Json.Serialization;

namespace PubSoft.Monitoring.Scheduling;

// Reflection-free metadata for the alert-rule job payload so the package stays Native AOT compatible.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(AlertRuleConfig))]
internal sealed partial class AlertJsonContext : JsonSerializerContext;
