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

    [Fact]
    public void MessagePack_EmitsFormatter_ForSinglePropertyValueObject()
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

        var result = RunGenerator(source, withMessagePack: true);

        Assert.Empty(result.Diagnostics);
        var emitted = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("CustomerIdMessagePackFormatter.g.cs", StringComparison.Ordinal));
        Assert.NotNull(emitted);
        var text = emitted!.ToString();
        Assert.Contains("IMessagePackFormatter<CustomerId>", text, StringComparison.Ordinal);
        Assert.Contains("reader.ReadInt32()", text, StringComparison.Ordinal);
        Assert.Contains("writer.Write(value.Value)", text, StringComparison.Ordinal);
        Assert.Contains("[MessagePackFormatter(typeof(CustomerIdMessagePackFormatter))]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MemoryPack_EmitsFormatter_ForSinglePropertyValueObject()
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

        var result = RunGenerator(source, withMemoryPack: true);

        Assert.Empty(result.Diagnostics);
        var emitted = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.EndsWith("CustomerIdMemoryPackFormatter.g.cs", StringComparison.Ordinal));
        Assert.NotNull(emitted);
        var text = emitted!.ToString();
        Assert.Contains("MemoryPackFormatter<CustomerId>", text, StringComparison.Ordinal);
        // MemoryPackCustomFormatterAttribute<TFormatter, T> is abstract and targets
        // properties/fields only — the canonical path for a hand-rolled type formatter
        // is MemoryPackFormatterProvider.Register<T>(formatter) at module init.
        Assert.Contains("[ModuleInitializer]", text, StringComparison.Ordinal);
        Assert.Contains("MemoryPackFormatterProvider.Register<CustomerId>(new CustomerIdMemoryPackFormatter())", text, StringComparison.Ordinal);
        Assert.Contains("writer.WriteValue<int>(value.Value)", text, StringComparison.Ordinal);
        Assert.Contains("reader.ReadValue<int>()", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MessagePack_EmitsNothing_WhenBackendNotReferenced()
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

        var result = RunGenerator(source, withSystemTextJson: false, withMessagePack: false, withMemoryPack: false);

        Assert.DoesNotContain(result.GeneratedTrees,
            t => t.FilePath.EndsWith("CustomerIdMessagePackFormatter.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void MultiProperty_ValueObject_NoEmission_AllBackends()
    {
        var source = """
            using ZeroAlloc.ValueObjects;
            namespace TestModels;

            [ValueObject]
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

        var result = RunGenerator(source, withSystemTextJson: true, withMessagePack: true, withMemoryPack: true);

        Assert.DoesNotContain(result.GeneratedTrees,
            t => t.FilePath.EndsWith("MoneySystemTextJsonConverter.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(result.GeneratedTrees,
            t => t.FilePath.EndsWith("MoneyMessagePackFormatter.g.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(result.GeneratedTrees,
            t => t.FilePath.EndsWith("MoneyMemoryPackFormatter.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void NonValueObject_PartialStruct_NoEmission()
    {
        var source = """
            namespace TestModels;

            // Note: no [ValueObject] attribute.
            public readonly partial struct Wrap
            {
                public int Value { get; }
                public Wrap(int value) => Value = value;
            }
            """;

        var result = RunGenerator(source, withSystemTextJson: true);

        Assert.Empty(result.GeneratedTrees.Where(t =>
            t.FilePath.EndsWith("WrapSystemTextJsonConverter.g.cs", StringComparison.Ordinal)));
    }

    private static GeneratorDriverRunResult RunGenerator(
        string source,
        bool withSystemTextJson = false,
        bool withMessagePack = false,
        bool withMemoryPack = false)
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
