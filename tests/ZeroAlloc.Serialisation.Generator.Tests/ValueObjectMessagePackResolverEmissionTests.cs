using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Serialisation.Generator;

namespace ZeroAlloc.Serialisation.Generator.Tests;

public class ValueObjectMessagePackResolverEmissionTests
{
    [Fact]
    public void Resolver_EmitsIFormatterResolverClass_ListingAllValueObjectsInAssembly()
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

        var result = RunGenerator(source, withMessagePack: true);

        Assert.Empty(result.Diagnostics);
        var emitted = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("ValueObjectMessagePackResolverExtensions.g.cs", StringComparison.Ordinal));
        Assert.NotNull(emitted);
        var text = emitted!.ToString();

        // Namespace + resolver class shape
        Assert.Contains("namespace ZeroAlloc.Serialisation.MessagePack;", text, StringComparison.Ordinal);
        Assert.Contains("internal sealed class ValueObjectMessagePackResolver : global::MessagePack.IFormatterResolver", text, StringComparison.Ordinal);
        Assert.Contains("public static ValueObjectMessagePackResolver Default { get; } = new();", text, StringComparison.Ordinal);

        // FormatterCache<T> generic-static-cache nested class
        Assert.Contains("private static class FormatterCache<T>", text, StringComparison.Ordinal);
        Assert.Contains("public static readonly global::MessagePack.Formatters.IMessagePackFormatter<T>?", text, StringComparison.Ordinal);

        // GetFormatter<T> + untyped lookup
        Assert.Contains("public global::MessagePack.Formatters.IMessagePackFormatter<T>? GetFormatter<T>()", text, StringComparison.Ordinal);
        Assert.Contains("FormatterCache<T>.Formatter", text, StringComparison.Ordinal);
        Assert.Contains("private static object? GetFormatterUntyped(global::System.Type type)", text, StringComparison.Ordinal);

        // One switch arm per value-object, each instantiating the FQN'd formatter
        Assert.Contains("if (type == typeof(global::TestModels.CustomerId))", text, StringComparison.Ordinal);
        Assert.Contains("return new global::TestModels.CustomerIdMessagePackFormatter();", text, StringComparison.Ordinal);
        Assert.Contains("if (type == typeof(global::TestModels.OrderId))", text, StringComparison.Ordinal);
        Assert.Contains("return new global::TestModels.OrderIdMessagePackFormatter();", text, StringComparison.Ordinal);

        // Fallback null
        Assert.Contains("return null;", text, StringComparison.Ordinal);

        // Extension method
        Assert.Contains("public static class ValueObjectMessagePackFormattersExtensions", text, StringComparison.Ordinal);
        Assert.Contains("public static global::MessagePack.MessagePackSerializerOptions AddZeroAllocValueObjectFormatters(this global::MessagePack.MessagePackSerializerOptions options)", text, StringComparison.Ordinal);
        Assert.Contains("global::MessagePack.Resolvers.CompositeResolver.Create", text, StringComparison.Ordinal);
        Assert.Contains("ValueObjectMessagePackResolver.Default", text, StringComparison.Ordinal);
        Assert.Contains("options.Resolver", text, StringComparison.Ordinal);
        Assert.Contains("options.WithResolver(", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolver_NotEmitted_WhenNoValueObjectsPresent()
    {
        var source = """
            namespace TestModels;

            public class Plain { public int Value { get; set; } }
            """;

        var result = RunGenerator(source, withMessagePack: true);

        Assert.DoesNotContain(result.GeneratedTrees,
            t => t.FilePath.EndsWith("ValueObjectMessagePackResolverExtensions.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void Resolver_NotEmitted_WhenMessagePackBackendNotReferenced()
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

        var result = RunGenerator(source); // all backend flags default false

        Assert.DoesNotContain(result.GeneratedTrees,
            t => t.FilePath.EndsWith("ValueObjectMessagePackResolverExtensions.g.cs", StringComparison.Ordinal));
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
