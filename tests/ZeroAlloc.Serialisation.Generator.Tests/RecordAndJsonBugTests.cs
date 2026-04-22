using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Serialisation.Generator.Tests;

public sealed class RecordAndJsonBugTests
{
    [Fact]
    public void SystemTextJson_GeneratesValidBody_WithNoSyntaxErrors()
    {
        var source = """
            using ZeroAlloc.Serialisation;
            using System.Text.Json.Serialization;
            namespace Demo;
            [ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
            public sealed class WeatherResponse
            {
                public string City { get; set; } = "";
                public double TemperatureC { get; set; }
            }
            [JsonSerializable(typeof(WeatherResponse))]
            internal partial class WeatherResponseContext : JsonSerializerContext { }
            """;

        var (generated, diagnostics) = Generate(source);
        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        Assert.Contains("public void Serialize", generated, System.StringComparison.Ordinal);
        // Must NOT be an expression body with a block expression (invalid C#)
        Assert.DoesNotContain("=> {", generated, System.StringComparison.Ordinal);
        // Compiling the generated source with the original must succeed
        AssertGeneratedCompiles(source);
    }

    [Fact]
    public void RecordClass_GeneratesSerializer()
    {
        var source = """
            using ZeroAlloc.Serialisation;
            using System.Text.Json.Serialization;
            namespace Demo;
            [ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
            public sealed record class WeatherResponseRecord(string City, double TemperatureC);
            [JsonSerializable(typeof(WeatherResponseRecord))]
            internal partial class WeatherResponseRecordContext : JsonSerializerContext { }
            """;

        var (generated, _) = Generate(source);
        Assert.Contains("WeatherResponseRecordSerializer", generated, System.StringComparison.Ordinal);
        Assert.Contains("ISerializer<Demo.WeatherResponseRecord>", generated, System.StringComparison.Ordinal);
    }

    [Fact]
    public void RecordStruct_GeneratesSerializer()
    {
        var source = """
            using ZeroAlloc.Serialisation;
            using System.Text.Json.Serialization;
            namespace Demo;
            [ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
            public readonly record struct PointRecord(int X, int Y);
            [JsonSerializable(typeof(PointRecord))]
            internal partial class PointRecordContext : JsonSerializerContext { }
            """;

        var (generated, _) = Generate(source);
        Assert.Contains("PointRecordSerializer", generated, System.StringComparison.Ordinal);
    }

    private static (string generated, IReadOnlyList<Diagnostic> diagnostics) Generate(string source)
    {
        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new SerializerGenerator())
            .RunGenerators(compilation);

        var result = driver.GetRunResult();
        var combined = string.Join(
            "\n\n",
            result.GeneratedTrees.Select(static t => t.GetText().ToString()));
        return (combined, result.Diagnostics.ToArray());
    }

    private static void AssertGeneratedCompiles(string originalSource)
    {
        var compilation = CreateCompilation(originalSource);
        var driver = CSharpGeneratorDriver.Create(new SerializerGenerator())
            .RunGenerators(compilation);

        var result = driver.GetRunResult();

        // Compile only the per-type serializer output (not DI/Dispatcher which require
        // Microsoft.Extensions.DependencyInjection at runtime). This is the file that
        // historically contained the invalid `=> { ... }` expression body.
        var serializerTrees = result.GeneratedTrees
            .Where(static t => t.FilePath.EndsWith("Serializer.g.cs", System.StringComparison.Ordinal)
                && !t.FilePath.Contains("SerializerDispatcher", System.StringComparison.Ordinal)
                && !t.FilePath.Contains("Extensions", System.StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(serializerTrees);

        // Synthesize global usings that the generator assumes are present (the hosting
        // project enables ImplicitUsings; the generator output relies on `System` being
        // in scope for `ReadOnlySpan<byte>`).
        const string GlobalUsings = "global using System;\n";

        var allTrees = new List<SyntaxTree>(serializerTrees)
        {
            CSharpSyntaxTree.ParseText(originalSource),
            CSharpSyntaxTree.ParseText(GlobalUsings),
        };

        var references = BuildReferences();
        var fullCompilation = CSharpCompilation.Create(
            "FullCompileTest",
            allTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var allDiagnostics = fullCompilation.GetDiagnostics();
        Assert.DoesNotContain(allDiagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var references = BuildReferences();

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static List<MetadataReference> BuildReferences()
    {
        var zeroAllocRef = MetadataReference.CreateFromFile(
            typeof(ZeroAlloc.Serialisation.ZeroAllocSerializableAttribute).Assembly.Location);

        return new List<MetadataReference>(Basic.Reference.Assemblies.Net100.References.All)
        {
            zeroAllocRef,
        };
    }
}
