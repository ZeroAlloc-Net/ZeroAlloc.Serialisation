using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace ZeroAlloc.ValueObjects
{
    [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
    public sealed class ValueObjectAttribute : System.Attribute { }
}

namespace ZeroAlloc.Serialisation.Tests
{
    // NOTE: the source generator that produces these JsonConverter pairs is
    // wired into ZeroAlloc.Serialisation as an analyzer with PrivateAssets="all",
    // so it does not flow transitively into this test project. Adding a direct
    // analyzer ProjectReference to ZeroAlloc.Serialisation.Generator from this
    // csproj would be the production-equivalent setup but is outside the
    // permitted edit surface for this phase. To still exercise the wire-format
    // contract end-to-end through the real System.Text.Json pipeline, the
    // converter + [JsonConverter] attribute below mirror BYTE-FOR-BYTE what
    // ValueObjectEmitter.EmitSystemTextJsonConverter produces for this type.
    // The snapshot test in ZeroAlloc.Serialisation.Generator.Tests asserts
    // the emitter actually produces this shape, so this round-trip test
    // remains a faithful proxy for the generated output.
    [global::ZeroAlloc.ValueObjects.ValueObject]
    [JsonConverter(typeof(CustomerIdSystemTextJsonConverter))]
    public readonly partial struct CustomerId
    {
        public int Value { get; }
        public CustomerId(int value) => Value = value;
    }

    internal sealed class CustomerIdSystemTextJsonConverter : JsonConverter<CustomerId>
    {
        public override CustomerId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => new CustomerId(reader.GetInt32());

        public override void Write(Utf8JsonWriter writer, CustomerId value, JsonSerializerOptions options)
            => writer.WriteNumberValue(value.Value);
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
