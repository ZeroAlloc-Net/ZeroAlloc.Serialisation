using MessagePack;

namespace ZeroAlloc.Serialisation.Benchmarks;

[MessagePackObject]
[ZeroAllocSerializable(SerializationFormat.MessagePack)]
public class GenMsgDto
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public string Name { get; set; } = "";
}
