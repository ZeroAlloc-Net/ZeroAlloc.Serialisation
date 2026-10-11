using System.Text.Json.Serialization;

namespace ZeroAlloc.Serialisation.Benchmarks;

[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
public class GenStjDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

#pragma warning disable MA0048 // GenBenchContext is intentionally co-located with GenStjDto
[JsonSerializable(typeof(GenStjDto))]
internal partial class GenBenchContext : JsonSerializerContext { }
#pragma warning restore MA0048
