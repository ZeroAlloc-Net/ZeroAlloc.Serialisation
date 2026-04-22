using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Serialisation.Generator.Tests;

/// <summary>
/// Shared test helper for running <see cref="SerializerGenerator"/> against an inline source snippet
/// and returning both the generated trees and the diagnostics the generator reported.
/// </summary>
internal static class GeneratorTestHost
{
    public static (ImmutableArray<SyntaxTree> GeneratedTrees, ImmutableArray<Diagnostic> Diagnostics) Generate(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var zeroAllocRef = MetadataReference.CreateFromFile(
            typeof(ZeroAlloc.Serialisation.ZeroAllocSerializableAttribute).Assembly.Location);

        var references = new List<MetadataReference>(Basic.Reference.Assemblies.Net100.References.All)
        {
            zeroAllocRef,
        };

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new SerializerGenerator())
            .RunGenerators(compilation);

        var result = driver.GetRunResult();
        return (result.GeneratedTrees, result.Diagnostics);
    }
}
