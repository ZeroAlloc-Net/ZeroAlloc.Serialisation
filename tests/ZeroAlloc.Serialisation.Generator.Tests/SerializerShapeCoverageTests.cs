using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Serialisation.Generator.Tests;

// Covers generator output shapes not exercised by SerializerGeneratorTests or
// RecordAndJsonBugTests: nested types, global-namespace types, partial classes
// split across multiple source files, and open-generic type behaviour.
public sealed class SerializerShapeCoverageTests
{
    [Fact]
    public void NestedType_GeneratesSerializer()
    {
        var source = """
            using ZeroAlloc.Serialisation;
            namespace Demo;
            public class Outer
            {
                [ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
                public sealed class Inner { public string Name { get; set; } = ""; }
            }
            """;

        var (generated, diagnostics) = Generate(source);

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("InnerSerializer", generated, System.StringComparison.Ordinal);
        // Inner's full type name must carry the outer type so the generated code
        // compiles even when the nested type is referenced from the enclosing namespace.
        Assert.Contains("Demo.Outer.Inner", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void GlobalNamespaceType_GeneratesSerializer()
    {
        var source = """
            using ZeroAlloc.Serialisation;
            [ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
            public sealed class Unnamespaced { public string V { get; set; } = ""; }
            """;

        var (generated, diagnostics) = Generate(source);

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("UnnamespacedSerializer", generated, System.StringComparison.Ordinal);
        // Guard against a stray `namespace ;` emission when the source type lives
        // in the global namespace (the `else ""` branch must suppress the declaration).
        Assert.DoesNotContain("namespace ;", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void PartialClassAcrossFiles_GeneratesSingleSerializer()
    {
        var sources = new[]
        {
            """
            using ZeroAlloc.Serialisation;
            namespace Demo;
            [ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
            public partial class SplitPoco { public string A { get; set; } = ""; }
            """,
            """
            namespace Demo;
            public partial class SplitPoco { public int B { get; set; } }
            """,
        };

        var result = GenerateFromMultiple(sources);

        Assert.DoesNotContain(result.Diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        var serializerHints = result.GeneratedTrees
            .Where(t => t.FilePath.Contains("SplitPocoSerializer.g.cs", System.StringComparison.Ordinal))
            .ToArray();
        Assert.Single(serializerHints);
    }

    // Current behaviour for open generics is undefined — the generator emits a
    // serializer referencing the open type (e.g. Demo.Wrapper<T>), which is not
    // valid runtime code. Skip the assertion until a dedicated diagnostic is added
    // (see issue #8). Keeping the test (skipped) documents the edge case.
    [Fact(Skip = "open generics: future diagnostic (issue #8)")]
    public void GenericOpenType_EmitsDiagnosticOrSkipsEmission()
    {
        var source = """
            using ZeroAlloc.Serialisation;
            namespace Demo;
            [ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
            public sealed class Wrapper<T> { public T? Value { get; set; } }
            """;

        var (_, diagnostics) = Generate(source);
        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (string generated, IReadOnlyList<Diagnostic> diagnostics) Generate(string source)
        => GeneratorTestHost.Generate(source);

    private static GeneratorDriverRunResult GenerateFromMultiple(string[] sources)
        => GeneratorTestHost.GenerateFromMultiple(sources);
}

internal static class GeneratorTestHost
{
    public static (string generated, IReadOnlyList<Diagnostic> diagnostics) Generate(string source)
    {
        var compilation = CreateCompilation(new[] { source });
        var driver = CSharpGeneratorDriver.Create(new SerializerGenerator())
            .RunGenerators(compilation);

        var result = driver.GetRunResult();
        var combined = string.Join(
            "\n\n",
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));
        return (combined, result.Diagnostics.ToArray());
    }

    public static GeneratorDriverRunResult GenerateFromMultiple(string[] sources)
    {
        var compilation = CreateCompilation(sources);
        var driver = CSharpGeneratorDriver.Create(new SerializerGenerator())
            .RunGenerators(compilation);
        return driver.GetRunResult();
    }

    private static CSharpCompilation CreateCompilation(string[] sources)
    {
        var trees = sources.Select(static s => CSharpSyntaxTree.ParseText(s)).ToArray();
        var zeroAllocRef = MetadataReference.CreateFromFile(
            typeof(ZeroAlloc.Serialisation.ZeroAllocSerializableAttribute).Assembly.Location);

        var references = new List<MetadataReference>(Basic.Reference.Assemblies.Net100.References.All)
        {
            zeroAllocRef,
        };

        return CSharpCompilation.Create(
            "TestAssembly",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
