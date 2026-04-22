using System.Text.Json.Serialization;

namespace ZeroAlloc.Serialisation.AotSmoke;

// The generator discovers this context (same compilation, [JsonSerializable(typeof(StjMessage))])
// and rewrites the emitted StjMessageSerializer to route through
// StjMessageContext.Default.StjMessage — fully AOT-safe, no reflection fallback.
[JsonSerializable(typeof(StjMessage))]
internal partial class StjMessageContext : JsonSerializerContext { }
