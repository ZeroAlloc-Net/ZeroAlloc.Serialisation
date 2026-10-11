using MemoryPack;

namespace ZeroAlloc.Serialisation.Benchmarks;

// A value type, for the dispatcher's generic overloads: the object-typed overloads box it.
[MemoryPackable]
[ZeroAllocSerializable(SerializationFormat.MemoryPack)]
public partial struct GenMpStructDto
{
    public int Id { get; set; }
    public string Name { get; set; }
}
