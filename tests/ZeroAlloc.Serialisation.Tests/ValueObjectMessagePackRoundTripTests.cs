using MessagePack;
using Xunit;

namespace ZeroAlloc.Serialisation.Tests;

public class ValueObjectMessagePackRoundTripTests
{
    [Fact]
    public void CustomerId_Roundtrips_Through_MessagePack_AsBareInteger()
    {
        var original = new CustomerId(42);
        var bytes = MessagePackSerializer.Serialize(original);
        var json = MessagePackSerializer.ConvertToJson(bytes);
        Assert.Equal("42", json);
        var deserialized = MessagePackSerializer.Deserialize<CustomerId>(bytes);
        Assert.Equal(original, deserialized);
    }
}
