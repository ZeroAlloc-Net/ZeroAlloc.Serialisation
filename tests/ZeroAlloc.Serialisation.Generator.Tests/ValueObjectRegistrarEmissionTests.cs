using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Serialisation.Generator;

namespace ZeroAlloc.Serialisation.Generator.Tests;

public class ValueObjectRegistrarEmissionTests
{
    [Fact]
    public void Registrar_EmitsExtensionMethod_ListingAllValueObjectsInAssembly()
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

            [ValueObject]
            public readonly partial struct OrderId
            {
                public int Value { get; }
                public OrderId(int value) => Value = value;
            }
            """;

        var result = RunGenerator(source, withSystemTextJson: true);

        Assert.Empty(result.Diagnostics);
        var emitted = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ValueObjectJsonConvertersExtensions.g.cs", StringComparison.Ordinal));
        Assert.NotNull(emitted);
        var text = emitted!.ToString();

        // Namespace + class shape
        Assert.Contains("namespace ZeroAlloc.Serialisation.SystemTextJson;", text, StringComparison.Ordinal);
        Assert.Contains("public static class ValueObjectJsonConvertersExtensions", text, StringComparison.Ordinal);
        Assert.Contains("public static global::System.Text.Json.JsonSerializerOptions AddZeroAllocValueObjectConverters(this global::System.Text.Json.JsonSerializerOptions options)", text, StringComparison.Ordinal);

        // Both value-objects registered via their FQN converter classes
        Assert.Contains("options.Converters.Add(new global::TestModels.CustomerIdSystemTextJsonConverter());", text, StringComparison.Ordinal);
        Assert.Contains("options.Converters.Add(new global::TestModels.OrderIdSystemTextJsonConverter());", text, StringComparison.Ordinal);

        // Returns options for chaining
        Assert.Contains("return options;", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Registrar_NotEmitted_WhenNoValueObjectsPresent()
    {
        var source = """
            namespace TestModels;

            public class Plain
            {
                public int Value { get; set; }
            }
            """;

        var result = RunGenerator(source, withSystemTextJson: true);

        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.EndsWith("ValueObjectJsonConvertersExtensions.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Registrar_NotEmitted_WhenSystemTextJsonBackendNotReferenced()
    {
        var source = """
            using ZeroAlloc.ValueObjects;
            namespace TestModels;

            [ValueObject]
            public readonly partial struct CustomerId
            {
                public int Value { get; }
                public CustomerId(int v) => Value = v;
            }
            """;

        var result = RunGenerator(source);

        Assert.DoesNotContain(result.GeneratedTrees, t => t.FilePath.EndsWith("ValueObjectJsonConvertersExtensions.g.cs", StringComparison.Ordinal));
    }

    private static GeneratorDriverRunResult RunGenerator(string source, bool withSystemTextJson = false, bool withMessagePack = false, bool withMemoryPack = false)
    {
        var valueObjectStub = """
            namespace ZeroAlloc.ValueObjects
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
                public sealed class ValueObjectAttribute : System.Attribute { }
            }
            """;

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        };
        if (withSystemTextJson)
            references.Add(MetadataReference.CreateFromFile(typeof(ZeroAlloc.Serialisation.SystemTextJson.SystemTextJsonSerializer<int>).Assembly.Location));
        if (withMessagePack)
            references.Add(MetadataReference.CreateFromFile(typeof(ZeroAlloc.Serialisation.MessagePack.MessagePackSerializer<int>).Assembly.Location));
        if (withMemoryPack)
            references.Add(MetadataReference.CreateFromFile(typeof(ZeroAlloc.Serialisation.MemoryPack.MemoryPackSerializer<int>).Assembly.Location));

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
