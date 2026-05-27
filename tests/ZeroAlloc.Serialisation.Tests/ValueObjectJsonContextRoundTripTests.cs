using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;
using ZeroAlloc.Serialisation.SystemTextJson;

namespace ZeroAlloc.Serialisation.Tests;

public class ValueObjectJsonContextRoundTripTests
{
    [Fact]
    public void CustomerDto_WithJsonContext_RoundTrips_AsBareInteger()
    {
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = JsonContextRoundTripContext.Default,
        };
        options.AddZeroAllocValueObjectConverters();

        var dto = new JsonContextCustomerDto(new JsonContextCustomerId(42), "alice");
        var json = JsonSerializer.Serialize(dto, options);

        Assert.Contains("\"Id\":42", json);
        Assert.DoesNotContain("\"value\"", json, System.StringComparison.OrdinalIgnoreCase);

        var roundTripped = JsonSerializer.Deserialize<JsonContextCustomerDto>(json, options);
        Assert.Equal(dto, roundTripped);
    }
}

[global::ZeroAlloc.ValueObjects.ValueObject]
public readonly partial struct JsonContextCustomerId
{
    public int Value { get; }
    public JsonContextCustomerId(int value) => Value = value;
}

public sealed record JsonContextCustomerDto(JsonContextCustomerId Id, string Name);

[JsonSerializable(typeof(JsonContextCustomerDto))]
[JsonSerializable(typeof(JsonContextCustomerId))]
internal sealed partial class JsonContextRoundTripContext : JsonSerializerContext { }
