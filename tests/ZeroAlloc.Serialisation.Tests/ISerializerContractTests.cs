using System.Buffers;
using Xunit;
using ZeroAlloc.Serialisation;

namespace ZeroAlloc.Serialisation.Tests;

// Verify the interface shape is correct and can be implemented without issues.
public sealed class FakeSerializer : ISerializer<int>
{
    public void Serialize(IBufferWriter<byte> writer, int value)
        => writer.GetSpan(4)[0] = (byte)value; // minimal impl

    public int Deserialize(ReadOnlySpan<byte> buffer)
        => buffer[0];
}

public class ISerializerContractTests
{
    [Fact]
    public void ISerializer_CanBeImplemented()
    {
        ISerializer<int> s = new FakeSerializer();
        Assert.NotNull(s);
    }

    [Fact]
    public void ZeroAllocSerializableAttribute_StoresFormat()
    {
        var attr = new ZeroAllocSerializableAttribute(SerializationFormat.MemoryPack);
        Assert.Equal(SerializationFormat.MemoryPack, attr.Format);
    }
}
