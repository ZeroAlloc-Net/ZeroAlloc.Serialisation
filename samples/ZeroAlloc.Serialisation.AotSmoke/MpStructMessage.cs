namespace ZeroAlloc.Serialisation.AotSmoke;

// Value-type MemoryPack target; see StjStructMessage.
[MemoryPack.MemoryPackable]
[ZeroAllocSerializable(SerializationFormat.MemoryPack)]
public readonly partial record struct MpStructMessage(int A, string B);
