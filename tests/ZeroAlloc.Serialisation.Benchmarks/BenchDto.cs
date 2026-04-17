using System.Text.Json.Serialization;
using MemoryPack;
using MessagePack;

namespace ZeroAlloc.Serialisation.Benchmarks;

[MemoryPackable]
[MessagePackObject]
public partial class BenchDto
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public string Name { get; set; } = "";
}

#pragma warning disable MA0048 // BenchContext is intentionally co-located with BenchDto
[JsonSerializable(typeof(BenchDto))]
internal partial class BenchContext : JsonSerializerContext { }
#pragma warning restore MA0048
