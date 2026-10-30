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
/// backends still get theirs. ZASZ015 warns that MemoryPack copies a struct value object that
/// holds no references as raw memory, so its validating factory never runs on that backend.
/// </summary>
public sealed class ValueObjectDiagnosticTests
{
    private const string ValueObjectStub = """
        namespace ZeroAlloc.ValueObjects
        {
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
            public sealed class ValueObjectAttribute : System.Attribute { }

            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
            public sealed class ValueObjectFactoryAttribute : System.Attribute
            {
                public ValueObjectFactoryAttribute(string methodName) { }
            }
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
                "Demo.FineMemoryPackFormatter.g.cs",
                "Demo.FineMessagePackFormatter.g.cs",
                "Demo.FineSystemTextJsonConverter.g.cs",
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
        Assert.Contains("Demo.Top.Hidden.IdSystemTextJsonConverter.g.cs", hints, StringComparer.Ordinal);
        Assert.Contains("Demo.Top.Hidden.IdMessagePackFormatter.g.cs", hints, StringComparer.Ordinal);
        Assert.DoesNotContain("Demo.Top.Hidden.IdMemoryPackFormatter.g.cs", hints, StringComparer.Ordinal);
        Assert.Contains("Demo.FineMemoryPackFormatter.g.cs", hints, StringComparer.Ordinal);

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
        Assert.Contains("Demo.Top.Hidden.IdSystemTextJsonConverter.g.cs", HintNames(result), StringComparer.Ordinal);
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
        Assert.Contains("Demo.Top.IdMemoryPackFormatter.g.cs", HintNames(result), StringComparer.Ordinal);
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

    [Theory]
    [InlineData("private")]
    [InlineData("protected")]
    [InlineData("private protected")]
    public void InaccessibleConstructorWithoutAFactory_ReportsZASZ014_InsteadOfCS0122(string modifier)
    {
        var declaration = $$"""
            [ValueObject]
            public sealed partial class Id
            {
                public int Value { get; }
                {{modifier}} Id(int value) => Value = value;
            }
            """;

        var (output, result) = Run(declaration, memoryPack: true);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("ZASZ014", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.StartsWith("[ValueObject] type 'Demo.Id' gets no generated serializers because it has no accessible constructor taking 'int'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Equal("ValueObject", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
        Assert.DoesNotContain(HintNames(result), static n => n.StartsWith("Demo.Id", StringComparison.Ordinal));
        AssertNoCompileErrors(output);
        Assert.DoesNotContain(output.GetDiagnostics(), static d => d.Id == "CS0122");
    }

    [Fact]
    public void StructWithNoConstructorTakingTheUnderlyingValue_ReportsZASZ014()
    {
        var declaration = """
            [ValueObject]
            public readonly partial struct Id
            {
                public int Value { get; init; }
            }
            """;

        var (_, result) = Run(declaration, memoryPack: false);

        Assert.Equal("ZASZ014", Assert.Single(result.Diagnostics).Id);
    }

    [Theory]
    [InlineData("TryCreate", "public static bool TryCreate(int input, out Id id) { id = default; return false; }")]
    [InlineData("Create", "public static Id Create(int input) => default;")]
    [InlineData("Make", "public static bool Make(int input, out Id id) { id = default; return false; }")]
    public void FactoryWithAPrivateConstructor_GeneratesEveryBackend(string name, string factory)
    {
        var declaration = $$"""
            [ValueObject]
            [ValueObjectFactory(nameof({{name}}))]
            public readonly partial struct Id
            {
                public int Value { get; }
                private Id(int value) => Value = value;
                {{factory}}
            }
            """;

        var (output, result) = Run(declaration, memoryPack: true);

        // An int struct with a factory under MemoryPack: ZASZ015 is the only diagnostic.
        Assert.Equal("ZASZ015", Assert.Single(result.Diagnostics).Id);
        var hints = HintNames(result);
        Assert.Contains("Demo.IdSystemTextJsonConverter.g.cs", hints, StringComparer.Ordinal);
        Assert.Contains("Demo.IdMessagePackFormatter.g.cs", hints, StringComparer.Ordinal);
        Assert.Contains("Demo.IdMemoryPackFormatter.g.cs", hints, StringComparer.Ordinal);
        foreach (var source in result.GeneratedSources.Where(static s => s.HintName.StartsWith("Demo.Id", StringComparison.Ordinal)))
        {
            var text = source.SourceText.ToString();
            Assert.DoesNotContain("new Id(", text, StringComparison.Ordinal);
            Assert.Contains($"Id.{name}(", text, StringComparison.Ordinal);
        }
        AssertNoCompileErrors(output);
    }

    [Fact]
    public void ConventionPrefersTryCreateOverCreate()
    {
        var declaration = """
            [ValueObject]
            public readonly partial struct Id
            {
                public int Value { get; }
                private Id(int value) => Value = value;
                public static Id Create(int input) => new Id(input);
                public static bool TryCreate(int input, out Id id) { id = new Id(input); return true; }
            }
            """;

        var (output, result) = Run(declaration, memoryPack: true);

        Assert.Equal("ZASZ015", Assert.Single(result.Diagnostics).Id);
        var json = result.GeneratedSources.Single(static s => s.HintName == "Demo.IdSystemTextJsonConverter.g.cs").SourceText.ToString();
        Assert.Contains("Id.TryCreate(", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Id.Create(", json, StringComparison.Ordinal);
        AssertNoCompileErrors(output);
    }

    [Theory]
    [InlineData("Missing", "")]
    [InlineData("Build", "private static bool Build(int input, out Id id) { id = default; return false; }")]
    [InlineData("Build", "public bool Build(int input, out Id id) { id = default; return false; }")]
    [InlineData("Build", "public static bool Build(long input, out Id id) { id = default; return false; }")]
    [InlineData("Build", "public static int Build(int input) => input;")]
    [InlineData("Build", "public static bool Build(int input) => true;")]
    public void ExplicitFactoryThatIsNotUsable_ReportsZASZ013_AndEmitsNothing(string name, string factory)
    {
        var declaration = $$"""
            [ValueObject]
            [ValueObjectFactory("{{name}}")]
            public readonly partial struct Id
            {
                public int Value { get; }
                public Id(int value) => Value = value;
                {{factory}}
            }
            """;

        var (_, result) = Run(declaration, memoryPack: true);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("ZASZ013", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.StartsWith($"[ValueObject] type 'Demo.Id' gets no generated serializers because [ValueObjectFactory] names '{name}'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.DoesNotContain(HintNames(result), static n => n.StartsWith("Demo.Id", StringComparison.Ordinal));
    }

    [Fact]
    public void ZASZ014_IsReportedEvenWhenAConventionalFactoryHasTheWrongShape()
    {
        var declaration = """
            [ValueObject]
            public readonly partial struct Id
            {
                public int Value { get; }
                private Id(int value) => Value = value;
                public static bool TryCreate(long input, out Id id) { id = default; return false; }
            }
            """;

        var (_, result) = Run(declaration, memoryPack: false);

        Assert.Equal("ZASZ014", Assert.Single(result.Diagnostics).Id);
    }

    [Fact]
    public void PublicConstructorWithoutAFactory_StillGeneratesWithTheConstructor()
    {
        var declaration = """
            [ValueObject]
            public readonly partial struct Id
            {
                public int Value { get; }
                public Id(int value) => Value = value;
            }
            """;

        var (output, result) = Run(declaration, memoryPack: true);

        Assert.Empty(result.Diagnostics);
        var json = result.GeneratedSources.Single(static s => s.HintName == "Demo.IdSystemTextJsonConverter.g.cs").SourceText.ToString();
        Assert.Contains("new Id(", json, StringComparison.Ordinal);
        AssertNoCompileErrors(output);
    }

    [Theory]
    [InlineData("readonly partial struct", "int", "", "TryCreate", "public static bool TryCreate(int input, out Id id) { id = new Id(input); return input > 0; }")]
    [InlineData("readonly partial struct", "global::System.Guid", "", "Create", "public static Id Create(global::System.Guid input) => new Id(input);")]
    [InlineData("readonly partial record struct", "global::System.TimeSpan", "", "TryCreate", "public static bool TryCreate(global::System.TimeSpan input, out Id id) { id = new Id(input); return true; }")]
    [InlineData("partial struct", "long", "[ValueObjectFactory(nameof(Make))]", "Make", "public static bool Make(long input, out Id id) { id = new Id(input); return true; }")]
    public void UnmanagedStructWithAFactory_UnderMemoryPack_ReportsZASZ015_AndStillEmitsTheFormatter(
        string kind, string underlying, string factoryAttribute, string factoryName, string factory)
    {
        var declaration = $$"""
            [ValueObject]
            {{factoryAttribute}}
            public {{kind}} Id
            {
                public {{underlying}} Value { get; }
                private Id({{underlying}} value) => Value = value;
                {{factory}}
            }
            """;

        var (output, result) = Run(declaration, memoryPack: true);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("ZASZ015", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Equal(
            $"MemoryPack copies [ValueObject] struct 'Demo.Id' as raw memory because it holds no references, so its factory '{factoryName}' never runs when MemoryPack deserializes it; System.Text.Json and MessagePack still call it",
            diagnostic.GetMessage());
        Assert.Equal("Id", diagnostic.Location.SourceTree!.GetText().ToString(diagnostic.Location.SourceSpan));
        Assert.Contains("source-generator.md#zasz015", diagnostic.Descriptor.HelpLinkUri, StringComparison.Ordinal);

        // The formatter is still emitted: it is correct, and used if MemoryPack ever asks for it.
        Assert.Contains("Demo.IdMemoryPackFormatter.g.cs", HintNames(result), StringComparer.Ordinal);
        AssertNoCompileErrors(output);
    }

    [Fact]
    public void UnmanagedStructWithAFactory_WithoutTheMemoryPackBackend_ReportsNothing()
    {
        var (output, result) = Run(UnmanagedStructWithTryCreate, memoryPack: false);

        Assert.Empty(result.Diagnostics);
        AssertNoCompileErrors(output);
    }

    [Theory]
    [InlineData("string")]
    [InlineData("byte[]")]
    public void ReferenceUnderlyingStructWithAFactory_UnderMemoryPack_ReportsNothing(string underlying)
    {
        var declaration = $$"""
            [ValueObject]
            public readonly partial struct Id
            {
                public {{underlying}} Value { get; }
                private Id({{underlying}} value) => Value = value;
                public static bool TryCreate({{underlying}} input, out Id id) { id = new Id(input); return input is not null; }
            }
            """;

        var (output, result) = Run(declaration, memoryPack: true);

        Assert.Empty(result.Diagnostics);
        AssertNoCompileErrors(output);
    }

    [Fact]
    public void IntStructWithAReferenceField_UnderMemoryPack_ReportsNothing()
    {
        // The layout decides, not the underlying type: a reference field sends MemoryPack to the formatter.
        var declaration = """
            [ValueObject]
            public readonly partial struct Id
            {
                private readonly string? _label;
                public int Value { get; }
                private Id(int value) { Value = value; _label = null; }
                public static bool TryCreate(int input, out Id id) { id = new Id(input); return id._label is null; }
            }
            """;

        var (output, result) = Run(declaration, memoryPack: true);

        Assert.Empty(result.Diagnostics);
        AssertNoCompileErrors(output);
    }

    [Fact]
    public void ClassWithAFactory_UnderMemoryPack_ReportsNothing()
    {
        var declaration = """
            [ValueObject]
            public sealed partial class Id
            {
                public int Value { get; }
                private Id(int value) => Value = value;
                public static bool TryCreate(int input, out Id? id) { id = new Id(input); return input > 0; }
            }
            """;

        var (output, result) = Run(declaration, memoryPack: true);

        Assert.Empty(result.Diagnostics);
        AssertNoCompileErrors(output);
    }

    [Fact]
    public void UnmanagedStructWithoutAFactory_UnderMemoryPack_ReportsNothing()
    {
        // The Supported type next to every declaration is this case too.
        var declaration = """
            [ValueObject]
            public readonly partial struct Id
            {
                public global::System.Guid Value { get; }
                public Id(global::System.Guid value) => Value = value;
            }
            """;

        var (output, result) = Run(declaration, memoryPack: true);

        Assert.Empty(result.Diagnostics);
        AssertNoCompileErrors(output);
    }

    [Fact]
    public void UnmanagedStructWithAFactoryInAHiddenContainer_ReportsZASZ006AndZASZ015()
    {
        // No formatter is registered, and MemoryPack would not call one anyway: both hold.
        var declaration = """
            public partial class Top
            {
                private partial class Hidden
                {
                    [ValueObject]
                    public readonly partial struct Id
                    {
                        public int Value { get; }
                        private Id(int value) => Value = value;
                        public static bool TryCreate(int input, out Id id) { id = new Id(input); return input > 0; }
                    }
                }
            }
            """;

        var (_, result) = Run(declaration, memoryPack: true);

        Assert.Equal(["ZASZ006", "ZASZ015"], result.Diagnostics.Select(static d => d.Id).OrderBy(static id => id, StringComparer.Ordinal), StringComparer.Ordinal);
    }

    private const string UnmanagedStructWithTryCreate = """
        [ValueObject]
        public readonly partial struct Id
        {
            public int Value { get; }
            private Id(int value) => Value = value;
            public static bool TryCreate(int input, out Id id) { id = new Id(input); return input > 0; }
        }
        """;

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
