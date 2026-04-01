using System.Buffers;
using MemoryPack;
using Xunit;
using ZeroAlloc.Serialisation.MemoryPack;

namespace ZeroAlloc.Serialisation.Tests;

[MemoryPackable]
public partial class SampleDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class MemoryPackSerializerTests
{
    private readonly MemoryPackSerializer<SampleDto> _serializer = new();

    [Fact]
    public void RoundTrip_PreservesValues()
    {
        var original = new SampleDto { Id = 42, Name = "Alice" };
        var buffer = new ArrayBufferWriter<byte>();

        _serializer.Serialize(buffer, original);
        var result = _serializer.Deserialize(buffer.WrittenSpan);

        Assert.NotNull(result);
        Assert.Equal(42, result.Id);
        Assert.Equal("Alice", result.Name);
    }

    [Fact]
    public void Serialize_WritesBytes()
    {
        var buffer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(buffer, new SampleDto { Id = 1, Name = "x" });
        Assert.True(buffer.WrittenCount > 0);
    }

    [Fact]
    public void Deserialize_EmptySpan_ReturnsDefault()
    {
        var result = _serializer.Deserialize(ReadOnlySpan<byte>.Empty);
        Assert.Null(result);
    }
}
