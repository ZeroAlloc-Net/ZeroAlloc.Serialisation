using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ZeroAlloc.Serialisation.Generator.Tests;

/// <summary>
/// The cached extraction results carry source locations, so an edit that does not touch a
/// serializable type must leave every tracked step and output cached, and an edit that moves one
/// must move its diagnostic.
/// </summary>
public sealed class SerializerIncrementalityTests
{
    // ZASZ003 on NoMp, ZASZ004 on Orphan, and one clean STJ type bound to a context.
    private const string TypesSource = """
        using ZeroAlloc.Serialisation;
        using System.Text.Json.Serialization;
        namespace Demo;

        [ZeroAllocSerializable(SerializationFormat.MemoryPack)]
        public sealed class NoMp { public string V { get; set; } = ""; }

        [ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
        public sealed class Orphan { public string V { get; set; } = ""; }

        [ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
        public sealed class Clean { public string V { get; set; } = ""; }

        [JsonSerializable(typeof(Clean))]
        internal partial class CleanContext : JsonSerializerContext { }
        """;

    // The tracking names the generator gives its steps, in ZeroAlloc.Serialisation.Generator.TrackingNames.
    private static readonly string[] GeneratorStepNames =
        ["ExtractionResults", "StjContextEntries", "BoundResults", "Models", "AllModels"];

    [Fact]
    public void UnrelatedEdit_LeavesEveryTrackedStepAndOutputCached()
    {
        var types = CSharpSyntaxTree.ParseText(TypesSource, path: "/src/Types.cs");
        var unrelated = CSharpSyntaxTree.ParseText(
            "namespace Demo; public class Unrelated { public int M() => 1; }", path: "/src/Unrelated.cs");
        var compilation = GeneratorTestHost.CreateCompilation([types, unrelated]);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SerializerGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

        driver = driver.RunGenerators(compilation);
        var first = driver.GetRunResult().Results[0];

        var edited = compilation.ReplaceSyntaxTree(
            unrelated,
            unrelated.WithChangedText(SourceText.From(
                "namespace Demo; public class Unrelated { public int M() => 2; public int N() => 3; }")));
        driver = driver.RunGenerators(edited);
        var second = driver.GetRunResult().Results[0];

        // Roslyn's own steps rerun on every edit; the generator's named steps must not change.
        foreach (var name in GeneratorStepNames)
        {
            Assert.True(second.TrackedSteps.TryGetValue(name, out var runSteps), $"Step '{name}' was not tracked.");
            AssertAllCachedOrUnchanged(name, runSteps);
        }

        Assert.NotEmpty(second.TrackedOutputSteps);
        foreach (var step in second.TrackedOutputSteps)
            AssertAllCachedOrUnchanged(step.Key, step.Value);

        // A cached output still reports its diagnostics at the same place, bound to the tree.
        Assert.Equal(Describe(first.Diagnostics), Describe(second.Diagnostics));
        Assert.Equal(
            ["ZASZ003", "ZASZ004"],
            second.Diagnostics.Select(d => d.Id).OrderBy(i => i, StringComparer.Ordinal),
            StringComparer.Ordinal);
        Assert.All(second.Diagnostics, d => Assert.Same(types, d.Location.SourceTree));
    }

    [Fact]
    public void EditAboveAType_MovesItsDiagnostic()
    {
        var types = CSharpSyntaxTree.ParseText(TypesSource, path: "/src/Types.cs");
        var compilation = GeneratorTestHost.CreateCompilation([types]);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SerializerGenerator());
        driver = driver.RunGenerators(compilation);
        var before = SerializerDiagnosticLocationTests.One(
            driver.GetRunResult().Diagnostics, d => string.Equals(d.Id, "ZASZ004", StringComparison.Ordinal));

        var moved = types.WithChangedText(SourceText.From(
            TypesSource.Replace("namespace Demo;", "namespace Demo;\n\n// two\n// more lines", StringComparison.Ordinal)));
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(types, moved));
        var after = SerializerDiagnosticLocationTests.One(
            driver.GetRunResult().Diagnostics, d => string.Equals(d.Id, "ZASZ004", StringComparison.Ordinal));

        Assert.Equal(
            before.Location.GetLineSpan().StartLinePosition.Line + 3,
            after.Location.GetLineSpan().StartLinePosition.Line);
        Assert.Same(moved, after.Location.SourceTree);
    }

    private static void AssertAllCachedOrUnchanged(string stepName, ImmutableArray<IncrementalGeneratorRunStep> runSteps)
    {
        foreach (var runStep in runSteps)
        {
            foreach (var (_, reason) in runStep.Outputs)
            {
                Assert.True(
                    reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged,
                    $"Step '{stepName}' produced output with reason {reason} after an unrelated edit.");
            }
        }
    }

    private static List<string> Describe(ImmutableArray<Diagnostic> diagnostics) =>
        diagnostics.Select(d => $"{d.Id} {d.Location.GetLineSpan()}").OrderBy(s => s, StringComparer.Ordinal).ToList();
}
