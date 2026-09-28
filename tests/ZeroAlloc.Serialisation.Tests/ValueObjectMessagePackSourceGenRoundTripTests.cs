using MessagePack;
using Xunit;
using ZeroAlloc.Serialisation.MessagePack;

namespace ZeroAlloc.Serialisation.Tests;

public class ValueObjectMessagePackSourceGenRoundTripTests
{
    [Fact]
    public void Dto_WithValueObjectField_RoundTrips_AsBareInteger_AfterFormattersHelperApplied()
    {
        // The load-bearing test for the 2.3.3 fix: when AddZeroAllocValueObjectFormatters
        // is called, the resolver chain serves our value-object formatter for
        // TestMpValueObjectId — NOT a default object-shape formatter.
        var options = MessagePackSerializerOptions.Standard
            .AddZeroAllocValueObjectFormatters();

        var dto = new TestMpDto { Id = new TestMpValueObjectId(42), Label = "alpha" };
        var bytes = MessagePackSerializer.Serialize(dto, options);
        var json = MessagePackSerializer.ConvertToJson(bytes);

        // MessagePack 3.x with [Key(int)] uses intkey array layout — ConvertToJson
        // renders the DTO as a JSON array. Without 2.3.3's resolver, the value-object
        // field would serialize as a wrapped sub-array [[42],"alpha"]; with the
        // resolver it collapses to a bare integer at position 0: [42,"alpha"].
        Assert.Equal("[42,\"alpha\"]", json);

        var roundTripped = MessagePackSerializer.Deserialize<TestMpDto>(bytes, options);
        Assert.Equal(dto.Id.Value, roundTripped.Id.Value);
        Assert.Equal(dto.Label, roundTripped.Label);
    }

    [Fact]
    public void NullableValueObject_IsServedByZeroAllocResolver_NotReflectionBuiltFormatter()
    {
        // Under NativeAOT, MessagePack's DynamicGenericResolver cannot build
        // NullableFormatter<VO> by reflection. The ZeroAlloc resolver must answer the
        // Nullable<VO> lookup itself with a closed StaticNullableFormatter.
        var options = MessagePackSerializerOptions.Standard
            .AddZeroAllocValueObjectFormatters();

        var formatter = options.Resolver.GetFormatter<TestMpValueObjectId?>();

        Assert.IsType<global::MessagePack.Formatters.StaticNullableFormatter<TestMpValueObjectId>>(formatter);
    }

    [Theory]
    [InlineData(42, "[42,\"alpha\"]")]
    [InlineData(null, "[null,\"alpha\"]")]
    public void Dto_WithNullableValueObjectField_RoundTrips(int? id, string expectedJson)
    {
        var options = MessagePackSerializerOptions.Standard
            .AddZeroAllocValueObjectFormatters();

        var dto = new TestMpNullableDto
        {
            Id = id is { } v ? new TestMpValueObjectId(v) : null,
            Label = "alpha",
        };
        var bytes = MessagePackSerializer.Serialize(dto, options);

        Assert.Equal(expectedJson, MessagePackSerializer.ConvertToJson(bytes));

        var roundTripped = MessagePackSerializer.Deserialize<TestMpNullableDto>(bytes, options);
        Assert.Equal(id, roundTripped.Id?.Value);
        Assert.Equal("alpha", roundTripped.Label);
    }
}

[global::ZeroAlloc.ValueObjects.ValueObject]
public readonly partial struct TestMpValueObjectId
{
    public int Value { get; }
    public TestMpValueObjectId(int value) => Value = value;
}

[MessagePackObject]
public sealed partial class TestMpDto
{
    [Key(0)] public TestMpValueObjectId Id { get; set; }
    [Key(1)] public string Label { get; set; } = "";
}

[MessagePackObject]
public sealed partial class TestMpNullableDto
{
    [Key(0)] public TestMpValueObjectId? Id { get; set; }
    [Key(1)] public string Label { get; set; } = "";
}
