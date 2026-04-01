using System.Buffers;
using MessagePack;
using Xunit;
using ZeroAlloc.Serialisation.MessagePack;

namespace ZeroAlloc.Serialisation.Tests;

[MessagePackObject]
public sealed class MsgPackDto
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public string Name { get; set; } = "";
}

public class MessagePackSerializerTests
{
    private readonly MessagePackSerializer<MsgPackDto> _serializer = new();

    [Fact]
    public void RoundTrip_PreservesValues()
    {
        var original = new MsgPackDto { Id = 7, Name = "Bob" };
        var buffer = new ArrayBufferWriter<byte>();

        _serializer.Serialize(buffer, original);
        var result = _serializer.Deserialize(buffer.WrittenSpan);

        Assert.NotNull(result);
        Assert.Equal(7, result.Id);
        Assert.Equal("Bob", result.Name);
    }

    [Fact]
    public void Serialize_WritesBytes()
    {
        var buffer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(buffer, new MsgPackDto { Id = 1, Name = "x" });
        Assert.True(buffer.WrittenCount > 0);
    }
}
