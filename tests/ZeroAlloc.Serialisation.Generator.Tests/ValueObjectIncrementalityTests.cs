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
/// The [ValueObject] pipeline carries value-only models, so an edit that touches no value object
/// must leave every tracked step and output cached, while an edit to a value object, or to the
/// backends the compilation references, must still change what is generated.
/// </summary>
public sealed class ValueObjectIncrementalityTests
{
    private const string ValueObjectStub = """
        namespace ZeroAlloc.ValueObjects
        {
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
            public sealed class ValueObjectAttribute : System.Attribute { }
        }
        """;

    private const string ValueObjectsSource = """
        using ZeroAlloc.ValueObjects;
        namespace Demo;

        [ValueObject]
        public readonly partial struct CustomerId
        {
            public int Value { get; }
            public CustomerId(int value) => Value = value;
        }

        [ValueObject]
        public partial record struct OrderRef(string Code);
        """;

    // The tracking names the generator gives its [ValueObject] steps, in
    // ZeroAlloc.Serialisation.Generator.TrackingNames.
    private static readonly string[] ValueObjectStepNames =
    [
        "ValueObjectResults",
        "ValueObjectModels",
        "ValueObjectBackends",
        "ValueObjectInput",
        "AllValueObjectModels",
        "ValueObjectRegistrarInput",
    ];

    // Every file the [ValueObject] outputs emit for the source above with all three backends.
    private static readonly string[] ExpectedHintNames =
    [
        "Demo.CustomerIdMemoryPackFormatter.g.cs",
        "Demo.CustomerIdMessagePackFormatter.g.cs",
        "Demo.CustomerIdSystemTextJsonConverter.g.cs",
        "Demo.OrderRefMemoryPackFormatter.g.cs",
        "Demo.OrderRefMessagePackFormatter.g.cs",
        "Demo.OrderRefSystemTextJsonConverter.g.cs",
        "ValueObjectJsonConvertersExtensions.g.cs",
        "ValueObjectJsonTypeInfoResolver.g.cs",
        "ValueObjectMessagePackResolverExtensions.g.cs",
    ];

    [Fact]
    public void UnrelatedEdit_LeavesEveryValueObjectStepAndOutputCached()
    {
        var valueObjects = CSharpSyntaxTree.ParseText(ValueObjectsSource, path: "/src/ValueObjects.cs");
        var unrelated = CSharpSyntaxTree.ParseText(
            "namespace Demo; public class Unrelated { public int M() => 1; }", path: "/src/Unrelated.cs");
        var compilation = CreateCompilation([valueObjects, unrelated], AllBackends());

        var driver = CreateTrackingDriver().RunGenerators(compilation);
        var first = driver.GetRunResult().Results[0];
        Assert.Equal(ExpectedHintNames, HintNames(first), StringComparer.Ordinal);

        var edited = compilation.ReplaceSyntaxTree(
            unrelated,
            unrelated.WithChangedText(SourceText.From(
                "namespace Demo; public class Unrelated { public int M() => 2; public int N() => 3; }")));
        driver = driver.RunGenerators(edited);
        var second = driver.GetRunResult().Results[0];

        // Roslyn's own steps rerun on every edit; the generator's named steps must not change.
        foreach (var name in ValueObjectStepNames)
        {
            Assert.True(second.TrackedSteps.TryGetValue(name, out var runSteps), $"Step '{name}' was not tracked.");
            AssertAllCachedOrUnchanged(name, runSteps);
        }

        Assert.NotEmpty(second.TrackedOutputSteps);
        foreach (var step in second.TrackedOutputSteps)
            AssertAllCachedOrUnchanged(step.Key, step.Value);

        Assert.Equal(GeneratedTexts(first), GeneratedTexts(second));
    }

    [Fact]
    public void EditToAValueObject_RegeneratesItsOutput()
    {
        var valueObjects = CSharpSyntaxTree.ParseText(ValueObjectsSource, path: "/src/ValueObjects.cs");
        var compilation = CreateCompilation([valueObjects], AllBackends());

        var driver = CreateTrackingDriver().RunGenerators(compilation);

        var renamed = valueObjects.WithChangedText(SourceText.From(
            ValueObjectsSource.Replace("int Value", "long Value", StringComparison.Ordinal)
                .Replace("int value", "long value", StringComparison.Ordinal)));
        driver = driver.RunGenerators(compilation.ReplaceSyntaxTree(valueObjects, renamed));
        var result = driver.GetRunResult().Results[0];

        var converter = Generated(result, "Demo.CustomerIdSystemTextJsonConverter.g.cs");
        Assert.Contains("reader.GetInt64()", converter, StringComparison.Ordinal);
        var formatter = Generated(result, "Demo.CustomerIdMemoryPackFormatter.g.cs");
        Assert.Contains("WriteValue<long>", formatter, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferencingABackend_EmitsItsOutputs()
    {
        var valueObjects = CSharpSyntaxTree.ParseText(ValueObjectsSource, path: "/src/ValueObjects.cs");
        var compilation = CreateCompilation([valueObjects], []);

        var driver = CreateTrackingDriver().RunGenerators(compilation);
        Assert.Empty(HintNames(driver.GetRunResult().Results[0]));

        driver = driver.RunGenerators(compilation.AddReferences(
            MetadataReference.CreateFromFile(
                typeof(ZeroAlloc.Serialisation.MessagePack.MessagePackSerializer<int>).Assembly.Location)));

        Assert.Equal(
            [
                "Demo.CustomerIdMessagePackFormatter.g.cs",
                "Demo.OrderRefMessagePackFormatter.g.cs",
                "ValueObjectMessagePackResolverExtensions.g.cs",
            ],
            HintNames(driver.GetRunResult().Results[0]),
            StringComparer.Ordinal);
    }

    private static GeneratorDriver CreateTrackingDriver() =>
        CSharpGeneratorDriver.Create(
            [new SerializerGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));

    private static MetadataReference[] AllBackends() =>
    [
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Serialisation.SystemTextJson.SystemTextJsonSerializer<int>).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Serialisation.MessagePack.MessagePackSerializer<int>).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(ZeroAlloc.Serialisation.MemoryPack.MemoryPackSerializer<int>).Assembly.Location),
    ];

    private static CSharpCompilation CreateCompilation(SyntaxTree[] trees, MetadataReference[] backends) =>
        CSharpCompilation.Create(
            "TestAssembly",
            [.. trees, CSharpSyntaxTree.ParseText(ValueObjectStub, path: "/src/ValueObjectStub.cs")],
            [.. Basic.Reference.Assemblies.Net100.References.All, .. backends],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

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

    private static List<string> HintNames(GeneratorRunResult result) =>
        result.GeneratedSources.Select(s => s.HintName).OrderBy(n => n, StringComparer.Ordinal).ToList();

    private static List<string> GeneratedTexts(GeneratorRunResult result) =>
        result.GeneratedSources
            .OrderBy(s => s.HintName, StringComparer.Ordinal)
            .Select(s => $"{s.HintName}\n{s.SourceText}")
            .ToList();

    private static string Generated(GeneratorRunResult result, string hintName) =>
        result.GeneratedSources.Single(s => string.Equals(s.HintName, hintName, StringComparison.Ordinal))
            .SourceText.ToString();
}
