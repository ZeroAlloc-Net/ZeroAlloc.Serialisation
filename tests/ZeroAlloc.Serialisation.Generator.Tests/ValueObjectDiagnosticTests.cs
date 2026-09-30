using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Serialisation.Generator.Tests;

/// <summary>
/// ZASZ005 rejects [ValueObject] shapes no generated serializer can support without reflection:
/// generic types, types nested in generic types and file-local types. ZASZ006 warns that a value
/// object inside a private or protected type gets no MemoryPack formatter, while the other
/// backends still get theirs.
/// </summary>
public sealed class ValueObjectDiagnosticTests
{
    private const string ValueObjectStub = """
        namespace ZeroAlloc.ValueObjects
        {
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
            public sealed class ValueObjectAttribute : System.Attribute { }
        }
        """;

    // A supported value object next to the rejected one, to show the rest still generates.
    private const string Supported = """

        [ValueObject]
        public readonly partial struct Fine
        {
            public int Value { get; }
            public Fine(int value) => Value = value;
        }
        """;

    [Theory]
    [InlineData(
        """
        public partial class Container<T>
        {
            [ValueObject]
            public sealed partial class Id
            {
                public int Value { get; }
                public Id(int value) => Value = value;
            }
        }
        """,
        "Demo.Container<T>.Id",
        "it is nested in generic type 'Demo.Container<T>'")]
    [InlineData(
        """
        public partial class Outer<TKey, TValue>
        {
            public partial class Middle
            {
                [ValueObject]
                public readonly partial struct Id
                {
                    public int Value { get; }
                    public Id(int value) => Value = value;
                }
            }
        }
        """,
        "Demo.Outer<TKey, TValue>.Middle.Id",
        "it is nested in generic type 'Demo.Outer<TKey, TValue>'")]
    [InlineData(
        """
        [ValueObject]
        public readonly partial struct Id<T>
        {
            public int Value { get; }
            public Id(int value) => Value = value;
        }
        """,
        "Demo.Id<T>",
        "it is generic")]
    [InlineData(
        """
        [ValueObject]
        file readonly partial struct Id
        {
            public int Value { get; }
            public Id(int value) => Value = value;
        }
        """,
        "Demo.Id",
        "it is file-local")]
    [InlineData(
        """
        file partial class Holder
        {
            [ValueObject]
            public readonly partial struct Id
            {
                public int Value { get; }
                public Id(int value) => Value = value;
            }
        }
        """,
        "Demo.Holder.Id",
        "it is nested in file-local type 'Demo.Holder'")]
    public void UnsupportedShape_ReportsZASZ005AtTheAttribute_AndEmitsNothingForIt(
        string declaration, string typeName, string reason)
    {
        var (output, result) = Run(declaration, memoryPack: true);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("ZASZ005", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.StartsWith($"[ValueObject] type '{typeName}' gets no generated serializers because {reason}", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.True(diagnostic.Location.IsInSource);
        Assert.Equal("ValueObject", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));

        // Only the supported type's files, and the registrars listing only it.
        Assert.Equal(
            [
                "FineMemoryPackFormatter.g.cs",
                "FineMessagePackFormatter.g.cs",
                "FineSystemTextJsonConverter.g.cs",
                "ValueObjectJsonConvertersExtensions.g.cs",
                "ValueObjectJsonTypeInfoResolver.g.cs",
                "ValueObjectMessagePackResolverExtensions.g.cs",
            ],
            HintNames(result));
        foreach (var source in result.GeneratedSources)
            Assert.DoesNotContain("Id", source.SourceText.ToString().Replace("Fine", "", StringComparison.Ordinal), StringComparison.Ordinal);

        AssertNoCompileErrors(output);
    }

    [Theory]
    [InlineData("private", "private")]
    [InlineData("protected", "protected")]
    [InlineData("private protected", "private protected")]
    public void HiddenContainer_ReportsZASZ006_AndSkipsOnlyMemoryPack(string modifier, string described)
    {
        var declaration = $$"""
            public partial class Top
            {
                {{modifier}} partial class Hidden
                {
                    [ValueObject]
                    public sealed partial class Id
                    {
                        public int Value { get; }
                        public Id(int value) => Value = value;
                    }
                }
            }
            """;

        var (output, result) = Run(declaration, memoryPack: true);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("ZASZ006", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(
            $"The MemoryPack formatter for [ValueObject] type 'Demo.Top.Hidden.Id' cannot be registered because its containing type 'Demo.Top.Hidden' is {described}; System.Text.Json and MessagePack serializers are still generated",
            diagnostic.GetMessage());
        Assert.Equal("Id", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));

        var hints = HintNames(result);
        Assert.Contains("Top.Hidden.IdSystemTextJsonConverter.g.cs", hints, StringComparer.Ordinal);
        Assert.Contains("Top.Hidden.IdMessagePackFormatter.g.cs", hints, StringComparer.Ordinal);
        Assert.DoesNotContain("Top.Hidden.IdMemoryPackFormatter.g.cs", hints, StringComparer.Ordinal);
        Assert.Contains("FineMemoryPackFormatter.g.cs", hints, StringComparer.Ordinal);

        AssertNoCompileErrors(output);
    }

    [Fact]
    public void HiddenContainer_WithoutTheMemoryPackBackend_ReportsNothing()
    {
        var declaration = """
            public partial class Top
            {
                private partial class Hidden
                {
                    [ValueObject]
                    public sealed partial class Id
                    {
                        public int Value { get; }
                        public Id(int value) => Value = value;
                    }
                }
            }
            """;

        var (output, result) = Run(declaration, memoryPack: false);

        Assert.Empty(result.Diagnostics);
        Assert.Contains("Top.Hidden.IdSystemTextJsonConverter.g.cs", HintNames(result), StringComparer.Ordinal);
        AssertNoCompileErrors(output);
    }

    [Fact]
    public void PrivateValueObjectInAPublicContainer_GetsEveryBackend_WithoutDiagnostics()
    {
        var declaration = """
            public partial class Top
            {
                [ValueObject]
                private sealed partial class Id
                {
                    public int Value { get; }
                    public Id(int value) => Value = value;
                }
            }
            """;

        var (output, result) = Run(declaration, memoryPack: true);

        Assert.Empty(result.Diagnostics);
        Assert.Contains("Top.IdMemoryPackFormatter.g.cs", HintNames(result), StringComparer.Ordinal);
        AssertNoCompileErrors(output);
    }

    [Fact]
    public void ZASZ005_CanBeSuppressedWithPragma()
    {
        var declaration = """
            public partial class Container<T>
            {
            #pragma warning disable ZASZ005
                [ValueObject]
            #pragma warning restore ZASZ005
                public sealed partial class Id
                {
                    public int Value { get; }
                    public Id(int value) => Value = value;
                }
            }
            """;

        var (output, _) = Run(declaration, memoryPack: true);

        Assert.DoesNotContain(output.GetDiagnostics(), static d => d.Id == "ZASZ005");
    }

    private static List<string> HintNames(GeneratorRunResult result) =>
        result.GeneratedSources.Select(s => s.HintName).OrderBy(n => n, StringComparer.Ordinal).ToList();

    private static void AssertNoCompileErrors(Compilation output)
    {
        var errors = output.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error && !d.Id.StartsWith("ZASZ", StringComparison.Ordinal))
            .Select(static d => d.ToString())
            .ToArray();
        Assert.True(errors.Length == 0, string.Join('\n', errors));
    }

    private static (Compilation Output, GeneratorRunResult Result) Run(string declaration, bool memoryPack)
    {
        var source = "using ZeroAlloc.ValueObjects;\nnamespace Demo;\n\n" + declaration + "\n" + Supported;

        var references = new List<MetadataReference>(Basic.Reference.Assemblies.Net100.References.All)
        {
            MetadataReference.CreateFromFile(typeof(ZeroAlloc.Serialisation.SystemTextJson.SystemTextJsonSerializer<int>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ZeroAlloc.Serialisation.MessagePack.MessagePackSerializer<int>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(global::MessagePack.MessagePackSerializer).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(global::MessagePack.KeyAttribute).Assembly.Location),
        };
        if (memoryPack)
        {
            references.Add(MetadataReference.CreateFromFile(typeof(ZeroAlloc.Serialisation.MemoryPack.MemoryPackSerializer<int>).Assembly.Location));
            references.Add(MetadataReference.CreateFromFile(typeof(global::MemoryPack.MemoryPackSerializer).Assembly.Location));
        }

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            [CSharpSyntaxTree.ParseText(source, path: "/src/Types.cs"), CSharpSyntaxTree.ParseText(ValueObjectStub)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new SerializerGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var result = driver.GetRunResult().Results[0];
        Assert.Null(result.Exception);
        return (output, result);
    }
}
