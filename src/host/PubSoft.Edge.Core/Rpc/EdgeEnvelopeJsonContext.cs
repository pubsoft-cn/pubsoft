using System.Text.Json;
using System.Text.Json.Serialization;

namespace PubSoft.Edge;

/// <summary>
/// Source-generated serializer metadata for the fixed Edge RPC envelopes. Using this
/// context keeps envelope serialization free of runtime reflection; caller params and
/// response payloads still need their own host-provided type metadata.
/// </summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(EdgeRpcRequest))]
[JsonSerializable(typeof(EdgeRpcResponse))]
public sealed partial class EdgeEnvelopeJsonContext : JsonSerializerContext;
