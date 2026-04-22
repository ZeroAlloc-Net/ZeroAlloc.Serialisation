using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using ZeroAlloc.Serialisation.SystemTextJson;

namespace ZeroAlloc.Serialisation.Tests;

// A consumer POCO that is both registered in a JsonSerializerContext
// (AOT source-gen path) and intended to be carried by this library's
// SystemTextJsonSerializer<T>. These tests confirm the two coexist
// and produce byte-for-byte equivalent output when the consumer's
// JsonTypeInfo is fed into our serializer.
[ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
public sealed class InteropPoco
{
    public string Name { get; set; } = "";
    public int    Count { get; set; }
}

[JsonSerializable(typeof(InteropPoco))]
internal sealed partial class InteropJsonContext : JsonSerializerContext { }

public sealed class JsonSerializerContextInteropTests
{
    [Fact]
    public void OurSerializer_RoundTripsCorrectly_WithConsumerContext()
    {
        var original = new InteropPoco { Name = "hello", Count = 42 };
        var buffer   = new ArrayBufferWriter<byte>();
        var serializer = new SystemTextJsonSerializer<InteropPoco>(
            InteropJsonContext.Default.InteropPoco);

        serializer.Serialize(buffer, original);
        var roundTripped = serializer.Deserialize(buffer.WrittenSpan);

        Assert.NotNull(roundTripped);
        Assert.Equal("hello", roundTripped!.Name);
        Assert.Equal(42,      roundTripped.Count);
    }

    [Fact]
    public void OurSerializer_ProducesSameBytesAs_DirectContextCall()
    {
        // Using JsonSerializerContext directly (the consumer's AOT path) should
        // produce byte-for-byte the same output as our serializer fed the same
        // JsonTypeInfo — our wrapper must not mutate the encoding.
        var original = new InteropPoco { Name = "hello", Count = 42 };

        var ours = SerializeWithOurs(original);
        var direct = JsonSerializer.SerializeToUtf8Bytes(
            original, InteropJsonContext.Default.InteropPoco);

        Assert.Equal(direct, ours);
    }

    private static byte[] SerializeWithOurs(InteropPoco value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        new SystemTextJsonSerializer<InteropPoco>(
            InteropJsonContext.Default.InteropPoco).Serialize(buffer, value);
        return buffer.WrittenSpan.ToArray();
    }
}
