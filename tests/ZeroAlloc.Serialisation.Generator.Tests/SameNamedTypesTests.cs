using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Serialisation.Generator.Tests;

/// <summary>
/// Types with the same name in different namespaces must get distinct hint names. A repeated
/// hint name makes Roslyn throw, and the generator loses its whole output.
/// </summary>
public sealed class SameNamedTypesTests
{
    private const string ValueObjectStub = """
        namespace ZeroAlloc.ValueObjects
        {
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
            public sealed class ValueObjectAttribute : System.Attribute { }
        }
        """;

    private const string SameNamedValueObjects = """
        using ZeroAlloc.ValueObjects;

        namespace Sales
        {
            [ValueObject]
            public readonly partial struct Id
            {
                public int Value { get; }
                public Id(int value) => Value = value;
            }

            public partial class Order
            {
                [ValueObject]
                public sealed partial record Line(string Value);
            }
        }

        namespace Billing
        {
            [ValueObject]
            public readonly partial struct Id
            {
                public long Value { get; }
                public Id(long value) => Value = value;
            }

            public partial class Order
            {
                [ValueObject]
                public sealed partial record Line(string Value);
            }
        }

        [ValueObject]
        public readonly partial struct Id
        {
            public int Value { get; }
            public Id(int value) => Value = value;
        }
        """;

    [Fact]
    public void ValueObjects_WithTheSameName_InDifferentNamespaces_AllGenerate()
    {
        var (output, result) = Run(SameNamedValueObjects);

        Assert.Equal(
            [
                "Billing.IdMemoryPackFormatter.g.cs",
                "Billing.IdMessagePackFormatter.g.cs",
                "Billing.IdSystemTextJsonConverter.g.cs",
                "Billing.Order.LineMemoryPackFormatter.g.cs",
                "Billing.Order.LineMessagePackFormatter.g.cs",
                "Billing.Order.LineSystemTextJsonConverter.g.cs",
                "IdMemoryPackFormatter.g.cs",
                "IdMessagePackFormatter.g.cs",
                "IdSystemTextJsonConverter.g.cs",
                "Sales.IdMemoryPackFormatter.g.cs",
                "Sales.IdMessagePackFormatter.g.cs",
                "Sales.IdSystemTextJsonConverter.g.cs",
                "Sales.Order.LineMemoryPackFormatter.g.cs",
                "Sales.Order.LineMessagePackFormatter.g.cs",
                "Sales.Order.LineSystemTextJsonConverter.g.cs",
                "ValueObjectJsonConvertersExtensions.g.cs",
                "ValueObjectJsonTypeInfoResolver.g.cs",
                "ValueObjectMessagePackResolverExtensions.g.cs",
            ],
            HintNames(result));
        AssertNoErrors(output);
    }

    [Fact]
    public void Serializables_WithTheSameName_InDifferentNamespaces_AllGenerate()
    {
        var source = """
            using ZeroAlloc.Serialisation;

            namespace Sales
            {
                [global::MessagePack.MessagePackObject]
                [ZeroAllocSerializable(SerializationFormat.MessagePack)]
                public sealed class Order
                {
                    [global::MessagePack.Key(0)] public int Id { get; set; }
                }
            }

            namespace Billing
            {
                [global::MessagePack.MessagePackObject]
                [ZeroAllocSerializable(SerializationFormat.MessagePack)]
                public sealed class Order
                {
                    [global::MessagePack.Key(0)] public int Id { get; set; }
                }
            }
            """;

        var (output, result) = Run(source);

        Assert.Equal(
            [
                "Billing.OrderSerializer.g.cs",
                "Billing.OrderSerializerExtensions.g.cs",
                "Sales.OrderSerializer.g.cs",
                "Sales.OrderSerializerExtensions.g.cs",
                "SerializerDispatcher.g.cs",
                "SerializerDispatcherExtensions.g.cs",
            ],
            HintNames(result));
        AssertNoErrors(output);
    }

    [Fact]
    public void GlobalNamespaceTypes_KeepTheirHintNames()
    {
        var source = """
            using ZeroAlloc.Serialisation;

            [global::MessagePack.MessagePackObject]
            [ZeroAllocSerializable(SerializationFormat.MessagePack)]
            public sealed class Order
            {
                [global::MessagePack.Key(0)] public int Id { get; set; }
            }
            """;

        var (_, result) = Run(source);

        Assert.Contains("OrderSerializer.g.cs", HintNames(result), StringComparer.Ordinal);
        Assert.Contains("OrderSerializerExtensions.g.cs", HintNames(result), StringComparer.Ordinal);
    }

    private static List<string> HintNames(GeneratorRunResult result) =>
        result.GeneratedSources.Select(s => s.HintName).OrderBy(n => n, StringComparer.Ordinal).ToList();

    private static void AssertNoErrors(Compilation output)
    {
        var errors = output.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .ToArray();
        Assert.True(errors.Length == 0, string.Join('\n', errors));
    }

    private static (Compilation Output, GeneratorRunResult Result) Run(string source)
    {
        var references = new List<MetadataReference>(Basic.Reference.Assemblies.Net100.References.All)
        {
            MetadataReference.CreateFromFile(typeof(ZeroAllocSerializableAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ZeroAlloc.Serialisation.SystemTextJson.SystemTextJsonSerializer<int>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ZeroAlloc.Serialisation.MessagePack.MessagePackSerializer<int>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(global::MessagePack.MessagePackSerializer).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(global::MessagePack.KeyAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ZeroAlloc.Serialisation.MemoryPack.MemoryPackSerializer<int>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(global::MemoryPack.MemoryPackSerializer).Assembly.Location),
            MetadataReference.CreateFromFile(
                typeof(global::Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
        };

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source), CSharpSyntaxTree.ParseText(ValueObjectStub)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new SerializerGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var result = driver.GetRunResult().Results[0];
        Assert.Null(result.Exception);
        return (output, result);
    }
}
