using System.Buffers;
using System.Text.Json.Serialization;
using Xunit;
using ZeroAlloc.Serialisation.SystemTextJson;

namespace ZeroAlloc.Serialisation.Tests;

public sealed class JsonDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

[JsonSerializable(typeof(JsonDto))]
internal partial class TestJsonContext : JsonSerializerContext { }

public class SystemTextJsonSerializerTests
{
    private readonly SystemTextJsonSerializer<JsonDto> _serializer =
        new(TestJsonContext.Default.JsonDto);

    [Fact]
    public void RoundTrip_PreservesValues()
    {
        var original = new JsonDto { Id = 3, Name = "Carol" };
        var buffer = new ArrayBufferWriter<byte>();

        _serializer.Serialize(buffer, original);
        var result = _serializer.Deserialize(buffer.WrittenSpan);

        Assert.NotNull(result);
        Assert.Equal(3, result.Id);
        Assert.Equal("Carol", result.Name);
    }

    [Fact]
    public void Serialize_WritesUtf8Json()
    {
        var buffer = new ArrayBufferWriter<byte>();
        _serializer.Serialize(buffer, new JsonDto { Id = 1, Name = "x" });
        Assert.Contains((byte)'{', buffer.WrittenSpan.ToArray());
    }

    [Fact]
    public void Deserialize_EmptySpan_ReturnsDefault()
    {
        var result = _serializer.Deserialize(ReadOnlySpan<byte>.Empty);
        Assert.Null(result);
    }
}
