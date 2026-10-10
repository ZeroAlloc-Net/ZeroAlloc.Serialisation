using System.Buffers;
using MemoryPack;
using MessagePack;
using MessagePack.Resolvers;
using Xunit;
using ZeroAlloc.Serialisation.MemoryPack;
using ZeroAlloc.Serialisation.MessagePack;

namespace ZeroAlloc.Serialisation.Tests;

[MemoryPackable]
public sealed partial record AotMemoryPackRecord(int Id, string Name);

[MessagePackObject]
public sealed class AotMessagePackDto
{
    [Key(0)] public int Id { get; set; }
    [Key(1)] public string Name { get; set; } = "";
}

[GeneratedMessagePackResolver]
internal sealed partial class AotTestResolver
{
}

public class AotSafeAdapterTests
{
    [Fact]
    public void MemoryPackableSerializer_RoundTripsMemoryPackableRecord()
    {
        var serializer = new MemoryPackableSerializer<AotMemoryPackRecord>();
        var buffer = new ArrayBufferWriter<byte>();

        serializer.Serialize(buffer, new AotMemoryPackRecord(5, "Eve"));
        var result = serializer.Deserialize(buffer.WrittenSpan);

        Assert.Equal(new AotMemoryPackRecord(5, "Eve"), result);
    }

    [Fact]
    public void MemoryPackableSerializer_Constructor_RegistersFormatter()
    {
        _ = new MemoryPackableSerializer<AotMemoryPackRecord>();

        Assert.True(MemoryPackFormatterProvider.IsRegistered<AotMemoryPackRecord>());
    }

    [Fact]
    public void MemoryPackableSerializer_EmptySpan_ReturnsDefault()
    {
        var serializer = new MemoryPackableSerializer<AotMemoryPackRecord>();
        Assert.Null(serializer.Deserialize(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void MessagePackSerializer_WithResolver_RoundTripsMessagePackObject()
    {
        var resolver = CompositeResolver.Create(AotTestResolver.Instance, BuiltinResolver.Instance);
        var serializer = new MessagePackSerializer<AotMessagePackDto>(resolver);
        var buffer = new ArrayBufferWriter<byte>();

        serializer.Serialize(buffer, new AotMessagePackDto { Id = 9, Name = "Zed" });
        var result = serializer.Deserialize(buffer.WrittenSpan);

        Assert.NotNull(result);
        Assert.Equal(9, result.Id);
        Assert.Equal("Zed", result.Name);
    }

    [Fact]
    public void MessagePackSerializer_NullResolver_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new MessagePackSerializer<AotMessagePackDto>((IFormatterResolver)null!));
    }
}
