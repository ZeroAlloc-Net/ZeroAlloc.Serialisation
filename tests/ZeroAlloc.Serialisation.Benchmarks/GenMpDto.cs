using MemoryPack;

namespace ZeroAlloc.Serialisation.Benchmarks;

[MemoryPackable]
[ZeroAllocSerializable(SerializationFormat.MemoryPack)]
public partial class GenMpDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
