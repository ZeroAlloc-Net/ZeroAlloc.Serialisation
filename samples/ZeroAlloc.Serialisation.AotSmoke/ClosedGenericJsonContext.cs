using System.Text.Json.Serialization;

namespace ZeroAlloc.Serialisation.AotSmoke;

// JsonTypeInfo for the closed generic types in ClosedGenericDeclarations.cs. The generator binds
// each generated serializer to the property STJ names after the closed type, such as EnvelopeOrder.
[JsonSerializable(typeof(StjEnvelope<StjOrder>))]
[JsonSerializable(typeof(StjPair<int, StjOrder>))]
[JsonSerializable(typeof(StjEnvelope<StjPair<int, StjOrder>>))]
internal sealed partial class ClosedGenericJsonContext : JsonSerializerContext { }
