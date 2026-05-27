using System.Text.Json.Serialization;

namespace ZeroAlloc.Serialisation.AotSmoke;

// The JsonSerializerContext that exposes the value-object container DTO to
// STJ's source-gen. Note: ValueObjectId is intentionally NOT in this context's
// [JsonSerializable] list — under the 2.3.2 design the per-assembly resolver
// is responsible for providing its typeinfo, and the registration would mask
// regressions in the resolver path.
[JsonSerializable(typeof(ValueObjectDto))]
internal partial class ValueObjectDtoContext : JsonSerializerContext { }
