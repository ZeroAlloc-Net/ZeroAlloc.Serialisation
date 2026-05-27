using System.Text.Json;
using Xunit;

namespace ZeroAlloc.ValueObjects
{
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
    public sealed class ValueObjectAttribute : System.Attribute { }
}

namespace ZeroAlloc.Serialisation.Tests
{
    [global::ZeroAlloc.ValueObjects.ValueObject]
    public readonly partial struct CustomerId
    {
        public int Value { get; }
        public CustomerId(int value) => Value = value;
    }

    public class ValueObjectSystemTextJsonRoundTripTests
    {
        [Fact]
        public void CustomerId_Roundtrips_Through_SystemTextJson_AsBareInteger()
        {
            var original = new CustomerId(42);
            var json = JsonSerializer.Serialize(original);
            Assert.Equal("42", json);
            var deserialized = JsonSerializer.Deserialize<CustomerId>(json);
            Assert.Equal(original, deserialized);
        }
    }
}
