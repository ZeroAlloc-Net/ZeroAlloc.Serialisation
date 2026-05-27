using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Serialisation.Generator;

namespace ZeroAlloc.Serialisation.Generator.Tests;

public class ValueObjectResolverEmissionTests
{
    [Fact]
    public void Resolver_EmitsIJsonTypeInfoResolverClass_ListingAllValueObjectsInAssembly()
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
            .FirstOrDefault(t => t.FilePath.EndsWith("ValueObjectJsonTypeInfoResolver.g.cs", StringComparison.Ordinal));
        Assert.NotNull(emitted);
        var text = emitted!.ToString();

        // Namespace + class shape
        Assert.Contains("namespace ZeroAlloc.Serialisation.SystemTextJson;", text, StringComparison.Ordinal);
        Assert.Contains("internal sealed class ValueObjectJsonTypeInfoResolver : global::System.Text.Json.Serialization.Metadata.IJsonTypeInfoResolver", text, StringComparison.Ordinal);
        Assert.Contains("public static ValueObjectJsonTypeInfoResolver Default { get; } = new();", text, StringComparison.Ordinal);

        // GetTypeInfo signature
        Assert.Contains("public global::System.Text.Json.Serialization.Metadata.JsonTypeInfo? GetTypeInfo(", text, StringComparison.Ordinal);
        Assert.Contains("global::System.Type type", text, StringComparison.Ordinal);
        Assert.Contains("global::System.Text.Json.JsonSerializerOptions options", text, StringComparison.Ordinal);

        // One switch arm per value-object, each calling JsonMetadataServices.CreateValueInfo<T>
        Assert.Contains("if (type == typeof(global::TestModels.CustomerId))", text, StringComparison.Ordinal);
        Assert.Contains("return global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateValueInfo<global::TestModels.CustomerId>(options, new global::TestModels.CustomerIdSystemTextJsonConverter());", text, StringComparison.Ordinal);
        Assert.Contains("if (type == typeof(global::TestModels.OrderId))", text, StringComparison.Ordinal);
        Assert.Contains("return global::System.Text.Json.Serialization.Metadata.JsonMetadataServices.CreateValueInfo<global::TestModels.OrderId>(options, new global::TestModels.OrderIdSystemTextJsonConverter());", text, StringComparison.Ordinal);

        // Fallback null
        Assert.Contains("return null;", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Registrar_AlsoInsertsResolverIntoChain()
    {
        // Verifies the 2.3.2 amplification of the 2.3.1 registrar: the method
        // body now contains the TypeInfoResolverChain.Insert call alongside
        // the existing Converters.Add lines.
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
        var registrar = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ValueObjectJsonConvertersExtensions.g.cs", StringComparison.Ordinal));
        Assert.NotNull(registrar);
        var text = registrar!.ToString();

        // 2.3.1 behaviour preserved
        Assert.Contains("options.Converters.Add(new global::TestModels.CustomerIdSystemTextJsonConverter());", text, StringComparison.Ordinal);

        // 2.3.2 addition
        Assert.Contains("options.TypeInfoResolverChain.Insert(0, global::ZeroAlloc.Serialisation.SystemTextJson.ValueObjectJsonTypeInfoResolver.Default);", text, StringComparison.Ordinal);
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
