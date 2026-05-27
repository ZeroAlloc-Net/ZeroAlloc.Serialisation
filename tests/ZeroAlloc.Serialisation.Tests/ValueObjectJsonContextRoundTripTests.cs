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

    [Fact]
    public void CustomerDto_WithJsonContext_AndCamelCasePolicy_StillRoundTripsAsBareInteger()
    {
        // Mirrors the ASP.NET Core default (services.ConfigureHttpJsonOptions
        // applies CamelCase). Verifies the value-object converter writes its
        // underlying primitive irrespective of the property naming policy
        // applied to the enclosing DTO.
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = JsonContextRoundTripContext.Default,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        options.AddZeroAllocValueObjectConverters();

        var dto = new JsonContextCustomerDto(new JsonContextCustomerId(42), "alice");
        var json = JsonSerializer.Serialize(dto, options);

        Assert.Contains("\"id\":42", json);
        Assert.DoesNotContain("\"value\"", json, System.StringComparison.OrdinalIgnoreCase);

        var roundTripped = JsonSerializer.Deserialize<JsonContextCustomerDto>(json, options);
        Assert.Equal(dto, roundTripped);
    }

    [Fact]
    public void GetTypeInfo_ForValueObject_AfterRegistrar_ReturnsNonNull()
    {
        // The load-bearing test for the 2.3.1 -> 2.3.2 fix: ASP.NET Core's
        // endpoint factory pre-resolves typeinfo for every binding type at
        // startup, hitting the resolver chain directly (not the Converters
        // list). Without the 2.3.2 resolver insert, this call throws
        // NotSupportedException("metadata not provided").
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = JsonContextRoundTripContext.Default,
        };
        options.AddZeroAllocValueObjectConverters();

        var typeInfo = options.GetTypeInfo(typeof(JsonContextCustomerId));

        Assert.NotNull(typeInfo);
        Assert.Equal(typeof(JsonContextCustomerId), typeInfo.Type);
    }

    [Fact]
    public void GetTypeInfo_ForUnrelatedType_FallsThroughToJsonContext()
    {
        // Confirms the resolver chain composes correctly: our resolver
        // returns null for non-value-object types, falling through to
        // JsonContext.Default which DOES have typeinfo for DTOs.
        var options = new JsonSerializerOptions
        {
            TypeInfoResolver = JsonContextRoundTripContext.Default,
        };
        options.AddZeroAllocValueObjectConverters();

        var typeInfo = options.GetTypeInfo(typeof(JsonContextCustomerDto));

        Assert.NotNull(typeInfo);
        Assert.Equal(typeof(JsonContextCustomerDto), typeInfo.Type);
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
