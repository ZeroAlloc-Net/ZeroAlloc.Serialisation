using MemoryPack;
using Xunit;

namespace ZeroAlloc.Serialisation.Tests;

public class ValueObjectMemoryPackRoundTripTests
{
    [Fact]
    public void CustomerId_Roundtrips_Through_MemoryPack()
    {
        var original = new CustomerId(42);
        var bytes = MemoryPackSerializer.Serialize(original);
        var deserialized = MemoryPackSerializer.Deserialize<CustomerId>(bytes);
        Assert.Equal(original, deserialized);
    }
}
