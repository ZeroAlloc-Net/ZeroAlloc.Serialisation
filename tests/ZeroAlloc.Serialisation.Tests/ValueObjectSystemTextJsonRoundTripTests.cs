using System.Text.Json;
using Xunit;

namespace ZeroAlloc.Serialisation.Tests
{
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
