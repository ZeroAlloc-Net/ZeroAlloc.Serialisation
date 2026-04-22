using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Serialisation.AotSmoke;

[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
public sealed class StjMessage
{
    public string Id { get; set; } = "";
    public int Value { get; set; }
}
