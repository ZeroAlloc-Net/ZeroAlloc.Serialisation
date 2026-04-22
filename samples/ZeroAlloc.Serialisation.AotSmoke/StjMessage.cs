using System.Text.Json.Serialization;
using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Serialisation.AotSmoke;

[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
public sealed class StjMessage
{
    public string Id { get; set; } = "";
    public int Value { get; set; }
}

// The generator now requires a JsonSerializerContext binding for every STJ-format
// type. Without this, ZASZ004 fires and emission is skipped. With it, the generated
// StjMessageSerializer calls JsonSerializer.Serialize(_jw, value, StjMessageContext.Default.StjMessage),
// which is fully AOT-safe.
[JsonSerializable(typeof(StjMessage))]
internal partial class StjMessageContext : JsonSerializerContext { }
