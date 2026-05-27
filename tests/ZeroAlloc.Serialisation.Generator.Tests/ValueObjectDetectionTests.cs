using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Serialisation.Generator;

namespace ZeroAlloc.Serialisation.Generator.Tests;

public class ValueObjectDetectionTests
{
    [Fact]
    public void SinglePropertyValueObject_IsDetected_AndReturnsUnderlyingProperty()
    {
        var source = """
            namespace ZeroAlloc.ValueObjects
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
                public sealed class ValueObjectAttribute : System.Attribute { }
            }

            namespace TestModels;

            [ZeroAlloc.ValueObjects.ValueObject]
            public readonly partial struct CustomerId
            {
                public int Value { get; }
                public CustomerId(int value) => Value = value;
            }
            """;

        var compilation = Compile(source);
        var candidate = compilation.GetTypeByMetadataName("TestModels.CustomerId")!;

        var result = ModelExtractor.TryGetTransparentValueObject(candidate);

        Assert.NotNull(result);
        Assert.Equal("CustomerId", result.Value.Type.Name);
        Assert.Equal("Value", result.Value.UnderlyingProperty.Name);
        Assert.Equal(SpecialType.System_Int32, result.Value.UnderlyingProperty.Type.SpecialType);
    }

    [Fact]
    public void MultiPropertyValueObject_ReturnsNull()
    {
        var source = """
            namespace ZeroAlloc.ValueObjects
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
                public sealed class ValueObjectAttribute : System.Attribute { }
            }

            namespace TestModels;

            [ZeroAlloc.ValueObjects.ValueObject]
            public readonly partial struct Money
            {
                public decimal Amount { get; }
                public string Currency { get; }
                public Money(decimal amount, string currency)
                {
                    Amount = amount;
                    Currency = currency;
                }
            }
            """;

        var compilation = Compile(source);
        var candidate = compilation.GetTypeByMetadataName("TestModels.Money")!;

        var result = ModelExtractor.TryGetTransparentValueObject(candidate);

        Assert.Null(result);
    }

    [Fact]
    public void NonValueObject_PartialStruct_ReturnsNull()
    {
        var source = """
            namespace TestModels;

            public readonly partial struct Wrap
            {
                public int Value { get; }
                public Wrap(int value) => Value = value;
            }
            """;

        var compilation = Compile(source);
        var candidate = compilation.GetTypeByMetadataName("TestModels.Wrap")!;

        var result = ModelExtractor.TryGetTransparentValueObject(candidate);

        Assert.Null(result);
    }

    [Fact]
    public void ReferencesSystemTextJson_TrueWhenAssemblyReferenced()
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText("class C { }") },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(ZeroAlloc.Serialisation.SystemTextJson.SystemTextJsonSerializer<int>).Assembly.Location),
            });

        Assert.True(ValueObjectEmitter.ReferencesSystemTextJson(compilation));
        Assert.False(ValueObjectEmitter.ReferencesMessagePack(compilation));
        Assert.False(ValueObjectEmitter.ReferencesMemoryPack(compilation));
    }

    [Fact]
    public void ReferencesSystemTextJson_FalseWhenAssemblyNotReferenced()
    {
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText("class C { }") },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });

        Assert.False(ValueObjectEmitter.ReferencesSystemTextJson(compilation));
        Assert.False(ValueObjectEmitter.ReferencesMessagePack(compilation));
        Assert.False(ValueObjectEmitter.ReferencesMemoryPack(compilation));
    }

    private static CSharpCompilation Compile(string source) =>
        CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source) },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
