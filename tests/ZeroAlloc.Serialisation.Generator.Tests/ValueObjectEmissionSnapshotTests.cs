using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Serialisation.Generator;

namespace ZeroAlloc.Serialisation.Generator.Tests;

public class ValueObjectEmissionSnapshotTests
{
    [Fact]
    public void SystemTextJson_EmitsConverter_ForSinglePropertyValueObject()
    {
        var source = """
            using ZeroAlloc.ValueObjects;
            namespace TestModels;

            [ValueObject]
            public readonly partial struct CustomerId
            {
                public int Value { get; }
                public CustomerId(int value) => Value = value;
            }
            """;

        var result = RunGenerator(source, withSystemTextJson: true);

        Assert.Empty(result.Diagnostics);
        var emitted = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("CustomerIdSystemTextJsonConverter.g.cs", StringComparison.Ordinal));
        Assert.NotNull(emitted);
        var text = emitted!.ToString();
        Assert.Contains("JsonConverter<CustomerId>", text, StringComparison.Ordinal);
        Assert.Contains("reader.GetInt32()", text, StringComparison.Ordinal);
        Assert.Contains("writer.WriteNumberValue(value.Value)", text, StringComparison.Ordinal);
        Assert.Contains("[JsonConverter(typeof(CustomerIdSystemTextJsonConverter))]", text, StringComparison.Ordinal);
    }

    private static GeneratorDriverRunResult RunGenerator(string source, bool withSystemTextJson = false)
    {
        // Stub the [ValueObject] attribute so the generator's FQN lookup matches.
        var valueObjectStub = """
            namespace ZeroAlloc.ValueObjects
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
                public sealed class ValueObjectAttribute : System.Attribute { }
            }
            """;

        var references = new System.Collections.Generic.List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        };
        if (withSystemTextJson)
            references.Add(MetadataReference.CreateFromFile(typeof(ZeroAlloc.Serialisation.SystemTextJson.SystemTextJsonSerializer<int>).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source), CSharpSyntaxTree.ParseText(valueObjectStub) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new SerializerGenerator();
        var driver = CSharpGeneratorDriver.Create(generator).RunGenerators(compilation);
        return driver.GetRunResult();
    }
}
