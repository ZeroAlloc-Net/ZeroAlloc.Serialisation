using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace ZeroAlloc.Serialisation.Generator.Tests;

/// <summary>
/// <c>[assembly: ZeroAllocSerializable(typeof(Envelope&lt;Order&gt;), format)]</c> declares a
/// closed generic type serializable, and it gets the serializer, DI registration and dispatcher
/// entry a non-generic <c>[ZeroAllocSerializable]</c> type gets. See issue #183.
/// </summary>
public sealed class ClosedGenericSerializableTests
{
    private const string MessagePackTypes = """
        namespace Demo
        {
            [global::MessagePack.MessagePackObject]
            public sealed class Order
            {
                [global::MessagePack.Key(0)] public int Id { get; set; }
            }

            [global::MessagePack.MessagePackObject]
            public sealed class Envelope<T>
            {
                [global::MessagePack.Key(0)] public string Id { get; set; } = "";
                [global::MessagePack.Key(1)] public T? Body { get; set; }
            }

            [global::MessagePack.MessagePackObject]
            public struct Pair<TLeft, TRight>
            {
                [global::MessagePack.Key(0)] public TLeft Left { get; set; }
                [global::MessagePack.Key(1)] public TRight Right { get; set; }
            }
        }
        """;

    [Fact]
    public void ClosedGenerics_GetASerializerDiRegistrationAndDispatcherEntryEach()
    {
        var (output, result) = Run(MessagePackTypes, """
            using ZeroAlloc.Serialisation;
            using Demo;

            [assembly: ZeroAllocSerializable(typeof(Envelope<Order>), SerializationFormat.MessagePack)]
            [assembly: ZeroAllocSerializable(typeof(Pair<int, Order>), SerializationFormat.MessagePack)]
            [assembly: ZeroAllocSerializable(typeof(Envelope<Pair<int, Order>>), SerializationFormat.MessagePack)]
            """);

        Assert.Equal(
            [
                "Demo.Envelope{Demo.Order}Serializer.g.cs",
                "Demo.Envelope{Demo.Order}SerializerExtensions.g.cs",
                "Demo.Envelope{Demo.Pair{int,Demo.Order}}Serializer.g.cs",
                "Demo.Envelope{Demo.Pair{int,Demo.Order}}SerializerExtensions.g.cs",
                "Demo.Pair{int,Demo.Order}Serializer.g.cs",
                "Demo.Pair{int,Demo.Order}SerializerExtensions.g.cs",
                "SerializerDispatcher.g.cs",
                "SerializerDispatcherExtensions.g.cs",
            ],
            HintNames(result));
        AssertNoErrors(output);
        Assert.Empty(result.Diagnostics);

        var serializer = Source(result, "Demo.Envelope{Demo.Order}Serializer.g.cs");
        Assert.Contains(
            "internal sealed class EnvelopeOfOrderSerializer : ISerializer<global::Demo.Envelope<global::Demo.Order>>",
            serializer,
            StringComparison.Ordinal);

        Assert.Contains("public static IServiceCollection AddEnvelopeOfOrderSerializer(", Source(result, "Demo.Envelope{Demo.Order}SerializerExtensions.g.cs"), StringComparison.Ordinal);
        Assert.Contains("public static IServiceCollection AddPairOfInt32AndOrderSerializer(", Source(result, "Demo.Pair{int,Demo.Order}SerializerExtensions.g.cs"), StringComparison.Ordinal);
        Assert.Contains("public static IServiceCollection AddEnvelopeOfPairOfInt32AndOrderSerializer(", Source(result, "Demo.Envelope{Demo.Pair{int,Demo.Order}}SerializerExtensions.g.cs"), StringComparison.Ordinal);

        // A struct construction returns the plain value type, as a non-generic struct does.
        Assert.Contains(
            "public global::Demo.Pair<int, global::Demo.Order> Deserialize(",
            Source(result, "Demo.Pair{int,Demo.Order}Serializer.g.cs"),
            StringComparison.Ordinal);

        var dispatcher = Source(result, "SerializerDispatcher.g.cs");
        Assert.Contains("case global::Demo.Envelope<global::Demo.Order> __e: new global::Demo.EnvelopeOfOrderSerializer()", dispatcher, StringComparison.Ordinal);
        Assert.Contains("if (type == typeof(global::Demo.Pair<int, global::Demo.Order>)) return new global::Demo.PairOfInt32AndOrderSerializer()", dispatcher, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Demo.Envelope<global::Demo.Pair<int, global::Demo.Order>>)", dispatcher, StringComparison.Ordinal);
    }

    [Fact]
    public void ClosedGenerics_AndNonGenericTypes_ShareOneDispatcher()
    {
        var (output, result) = Run(MessagePackTypes, """
            using ZeroAlloc.Serialisation;
            using Demo;

            [assembly: ZeroAllocSerializable(typeof(Envelope<Order>), SerializationFormat.MessagePack)]

            namespace Demo
            {
                [global::MessagePack.MessagePackObject]
                [ZeroAllocSerializable(SerializationFormat.MessagePack)]
                public sealed class Invoice
                {
                    [global::MessagePack.Key(0)] public int Id { get; set; }
                }
            }
            """);

        AssertNoErrors(output);
        var dispatcher = Source(result, "SerializerDispatcher.g.cs");
        Assert.Contains("typeof(global::Demo.Invoice)", dispatcher, StringComparison.Ordinal);
        Assert.Contains("typeof(global::Demo.Envelope<global::Demo.Order>)", dispatcher, StringComparison.Ordinal);
        Assert.Single(result.GeneratedSources, s => string.Equals(s.HintName, "SerializerDispatcher.g.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void ClosedGenerics_OfTheSameDefinition_AcrossTwoFiles_AllGenerate()
    {
        var (output, result) = Run(
            MessagePackTypes,
            """
            using ZeroAlloc.Serialisation;
            [assembly: ZeroAllocSerializable(typeof(Demo.Envelope<Demo.Order>), SerializationFormat.MessagePack)]
            """,
            """
            using ZeroAlloc.Serialisation;
            [assembly: ZeroAllocSerializable(typeof(Demo.Envelope<string>), SerializationFormat.MessagePack)]
            """);

        AssertNoErrors(output);
        Assert.Contains("Demo.Envelope{Demo.Order}Serializer.g.cs", HintNames(result), StringComparer.Ordinal);
        Assert.Contains("Demo.Envelope{string}Serializer.g.cs", HintNames(result), StringComparer.Ordinal);
        Assert.Contains("AddEnvelopeOfStringSerializer", Source(result, "Demo.Envelope{string}SerializerExtensions.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ClosedGeneric_OfAMemoryPackType_RoutesThroughMemoryPack()
    {
        var (output, result) = Run("""
            using ZeroAlloc.Serialisation;

            [assembly: ZeroAllocSerializable(typeof(Demo.MpEnvelope<Demo.MpOrder>), SerializationFormat.MemoryPack)]

            namespace Demo
            {
                [global::MemoryPack.MemoryPackable]
                public sealed partial class MpOrder { public int Id { get; set; } }

                [global::MemoryPack.MemoryPackable]
                public sealed partial class MpEnvelope<T> { public T? Body { get; set; } }
            }
            """);

        AssertNoErrors(output);
        Assert.Empty(result.Diagnostics);
        Assert.Contains(
            "global::MemoryPack.MemoryPackSerializer.Deserialize<global::Demo.MpEnvelope<global::Demo.MpOrder>>(buffer)",
            Source(result, "Demo.MpEnvelope{Demo.MpOrder}Serializer.g.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ClosedGeneric_OfASystemTextJsonType_BindsToTheContextPropertyStjGenerates()
    {
        var (_, result) = Run("""
            using System.Text.Json.Serialization;
            using ZeroAlloc.Serialisation;

            [assembly: ZeroAllocSerializable(typeof(Demo.Envelope<Demo.Order>), SerializationFormat.SystemTextJson)]
            [assembly: ZeroAllocSerializable(typeof(Demo.Pair<int, Demo.Order>), SerializationFormat.SystemTextJson)]
            [assembly: ZeroAllocSerializable(typeof(Demo.Envelope<int?>), SerializationFormat.SystemTextJson)]
            [assembly: ZeroAllocSerializable(typeof(Demo.Envelope<Demo.Order[]>), SerializationFormat.SystemTextJson)]
            [assembly: ZeroAllocSerializable(typeof(Demo.Envelope<string>), SerializationFormat.SystemTextJson)]

            namespace Demo
            {
                public sealed class Order { public int Id { get; set; } }
                public sealed class Envelope<T> { public T? Body { get; set; } }
                public sealed class Pair<TLeft, TRight> { public TLeft? Left { get; set; } public TRight? Right { get; set; } }

                [JsonSerializable(typeof(Envelope<Order>))]
                [JsonSerializable(typeof(Pair<int, Order>))]
                [JsonSerializable(typeof(Envelope<int?>))]
                [JsonSerializable(typeof(Envelope<Order[]>))]
                [JsonSerializable(typeof(Envelope<string>), TypeInfoPropertyName = "StringEnvelope")]
                internal sealed partial class AppJsonContext : JsonSerializerContext { }
            }
            """);

        Assert.Empty(result.Diagnostics);
        Assert.Contains("global::Demo.AppJsonContext.Default.EnvelopeOrder)", Source(result, "Demo.Envelope{Demo.Order}Serializer.g.cs"), StringComparison.Ordinal);
        Assert.Contains("global::Demo.AppJsonContext.Default.PairInt32Order)", Source(result, "Demo.Pair{int,Demo.Order}Serializer.g.cs"), StringComparison.Ordinal);
        Assert.Contains("global::Demo.AppJsonContext.Default.EnvelopeNullableInt32)", Source(result, "Demo.Envelope{System.Nullable{int}}Serializer.g.cs"), StringComparison.Ordinal);
        Assert.Contains("global::Demo.AppJsonContext.Default.EnvelopeOrderArray)", Source(result, "Demo.Envelope{Demo.Order[]}Serializer.g.cs"), StringComparison.Ordinal);
        Assert.Contains("global::Demo.AppJsonContext.Default.StringEnvelope)", Source(result, "Demo.Envelope{string}Serializer.g.cs"), StringComparison.Ordinal);
        Assert.Contains("AddEnvelopeOfNullableOfInt32Serializer", Source(result, "Demo.Envelope{System.Nullable{int}}SerializerExtensions.g.cs"), StringComparison.Ordinal);
        Assert.Contains("AddEnvelopeOfOrderArraySerializer", Source(result, "Demo.Envelope{Demo.Order[]}SerializerExtensions.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ClosedGeneric_OfASystemTextJsonType_WithoutAContextEntry_ReportsZASZ004AndGeneratesNothing()
    {
        var (_, result) = Run("""
            using ZeroAlloc.Serialisation;

            [assembly: ZeroAllocSerializable(typeof(Demo.Envelope<Demo.Order>), SerializationFormat.SystemTextJson)]

            namespace Demo
            {
                public sealed class Order { public int Id { get; set; } }
                public sealed class Envelope<T> { public T? Body { get; set; } }
            }
            """);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("ZASZ004", diagnostic.Id);
        Assert.Contains("'Demo.Envelope<Demo.Order>'", Message(diagnostic), StringComparison.Ordinal);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void ClosedGeneric_WithoutTheFormatAttribute_ReportsZASZ003AndStillGenerates()
    {
        var (_, result) = Run("""
            using ZeroAlloc.Serialisation;

            [assembly: ZeroAllocSerializable(typeof(Demo.Envelope<int>), SerializationFormat.MessagePack)]

            namespace Demo
            {
                public sealed class Envelope<T> { public T? Body { get; set; } }
            }
            """);

        Assert.Equal("ZASZ003", Assert.Single(result.Diagnostics).Id);
        Assert.Contains("Demo.Envelope{int}Serializer.g.cs", HintNames(result), StringComparer.Ordinal);
    }

    [Fact]
    public void ClosedGeneric_NestedInAGenericType_UsesTheOuterTypeArguments()
    {
        var (output, result) = Run("""
            using ZeroAlloc.Serialisation;

            [assembly: ZeroAllocSerializable(typeof(Demo.Outer<int>.Inner), SerializationFormat.MessagePack)]

            namespace Demo
            {
                public sealed class Outer<T>
                {
                    [global::MessagePack.MessagePackObject]
                    public sealed class Inner { [global::MessagePack.Key(0)] public T? Value { get; set; } }
                }
            }
            """);

        AssertNoErrors(output);
        Assert.Empty(result.Diagnostics);
        Assert.Contains(
            "internal sealed class InnerOfInt32Serializer : ISerializer<global::Demo.Outer<int>.Inner>",
            Source(result, "Demo.Outer{int}.InnerSerializer.g.cs"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void UnresolvedTypeof_IsLeftToTheCompiler()
    {
        var (_, result) = Run("""
            using ZeroAlloc.Serialisation;
            [assembly: ZeroAllocSerializable(typeof(Demo.Missing<int>), SerializationFormat.MessagePack)]
            [assembly: ZeroAllocSerializable(typeof(System.Collections.Generic.List<Demo.Missing>), SerializationFormat.MessagePack)]
            """);

        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void ZASZ007_OpenGenericTypeof_IsReportedAndGeneratesNothing()
    {
        var (_, result) = Run(MessagePackTypes, """
            using ZeroAlloc.Serialisation;
            [assembly: ZeroAllocSerializable(typeof(Demo.Envelope<>), SerializationFormat.MessagePack)]
            [assembly: ZeroAllocSerializable(typeof(Demo.Pair<,>), SerializationFormat.MessagePack)]
            """);

        Assert.Equal(["ZASZ007", "ZASZ007"], Ids(result));
        Assert.All(result.Diagnostics, static d => Assert.Equal(DiagnosticSeverity.Error, d.Severity));
        Assert.Contains(result.Diagnostics, static d => Message(d).Contains("'Demo.Envelope<>'", StringComparison.Ordinal));
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void ZASZ008_NonGenericTypeThroughTheAssemblyForm_IsReportedAndGeneratesNothing()
    {
        var (_, result) = Run(MessagePackTypes, """
            using ZeroAlloc.Serialisation;
            [assembly: ZeroAllocSerializable(typeof(Demo.Order), SerializationFormat.MessagePack)]
            [assembly: ZeroAllocSerializable(typeof(int[]), SerializationFormat.MessagePack)]
            """);

        Assert.Equal(["ZASZ008", "ZASZ008"], Ids(result));
        Assert.Contains(
            result.Diagnostics,
            static d => Message(d).Contains("'Demo.Order'", StringComparison.Ordinal)
                && Message(d).Contains("[ZeroAllocSerializable(SerializationFormat.MessagePack)]", StringComparison.Ordinal));
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void ZASZ009_DuplicateAssemblyDeclarations_AreReported_AndTheFirstGenerates()
    {
        var (output, result) = Run(
            MessagePackTypes,
            """
            using ZeroAlloc.Serialisation;
            [assembly: ZeroAllocSerializable(typeof(Demo.Envelope<Demo.Order>), SerializationFormat.MessagePack)]
            [assembly: ZeroAllocSerializable(typeof(Demo.Envelope<Demo.Order>), SerializationFormat.MessagePack)]
            """,
            """
            using ZeroAlloc.Serialisation;
            [assembly: ZeroAllocSerializable(typeof(Demo.Envelope<Demo.Order>), SerializationFormat.MemoryPack)]
            """);

        Assert.Equal(["ZASZ009", "ZASZ009"], Ids(result));
        Assert.All(result.Diagnostics, static d => Assert.Contains("'Demo.Envelope<Demo.Order>' is already declared serializable", Message(d), StringComparison.Ordinal));
        AssertNoErrors(output);
        Assert.Contains("MessagePackSerializer", Source(result, "Demo.Envelope{Demo.Order}Serializer.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ZASZ009_NullableAnnotations_DoNotMakeADifferentType()
    {
        var (_, result) = Run(MessagePackTypes, """
            #nullable enable
            using ZeroAlloc.Serialisation;
            [assembly: ZeroAllocSerializable(typeof(Demo.Envelope<Demo.Order>), SerializationFormat.MessagePack)]
            [assembly: ZeroAllocSerializable(typeof(Demo.Envelope<Demo.Order?>), SerializationFormat.MessagePack)]
            """);

        Assert.Equal(["ZASZ009"], Ids(result));
    }

    [Fact]
    public void ZASZ009_RepeatedTypeLevelDeclarations_AcrossPartialParts_AreReported_AndTheTypeGeneratesOnce()
    {
        var (output, result) = Run("""
            using ZeroAlloc.Serialisation;
            namespace Demo;

            [global::MessagePack.MessagePackObject]
            [ZeroAllocSerializable(SerializationFormat.MessagePack)]
            [ZeroAllocSerializable(SerializationFormat.MessagePack)]
            public sealed partial class Invoice
            {
                [global::MessagePack.Key(0)] public int Id { get; set; }
            }

            [ZeroAllocSerializable(SerializationFormat.MemoryPack)]
            public sealed partial class Invoice { }
            """);

        Assert.Equal(["ZASZ009", "ZASZ009"], Ids(result));
        Assert.Null(result.Exception);
        AssertNoErrors(output);
        Assert.Contains("MessagePackSerializer", Source(result, "Demo.InvoiceSerializer.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ZASZ010_TypeFormOnATypeDeclaration_AndFormatFormOnTheAssembly_AreReported()
    {
        var (_, result) = Run(MessagePackTypes, """
            using ZeroAlloc.Serialisation;
            [assembly: ZeroAllocSerializable(SerializationFormat.MessagePack)]

            namespace Demo
            {
                [ZeroAllocSerializable(typeof(Envelope<Order>), SerializationFormat.MessagePack)]
                public sealed class Misplaced { }
            }
            """);

        Assert.Equal(["ZASZ010", "ZASZ010"], Ids(result));
        Assert.Contains(result.Diagnostics, static d => Message(d).StartsWith("[assembly: ZeroAllocSerializable(format)] names no type", StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, static d => Message(d).StartsWith("[ZeroAllocSerializable(typeof(...), format)] is only valid on the assembly", StringComparison.Ordinal));
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void ZASZ011_ClosedGenericsWhoseGeneratedNamesCollide_AreReported_AndTheFirstGenerates()
    {
        var (output, result) = Run(MessagePackTypes, """
            using ZeroAlloc.Serialisation;

            [assembly: ZeroAllocSerializable(typeof(Demo.Envelope<Sales.Order>), SerializationFormat.MessagePack)]
            [assembly: ZeroAllocSerializable(typeof(Demo.Envelope<Billing.Order>), SerializationFormat.MessagePack)]

            namespace Sales { [global::MessagePack.MessagePackObject] public sealed class Order { [global::MessagePack.Key(0)] public int Id { get; set; } } }
            namespace Billing { [global::MessagePack.MessagePackObject] public sealed class Order { [global::MessagePack.Key(0)] public int Id { get; set; } } }
            """);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("ZASZ011", diagnostic.Id);
        Assert.Equal(
            "'Demo.Envelope<Billing.Order>' would get the generated names EnvelopeOfOrderSerializer and AddEnvelopeOfOrderSerializer, which 'Demo.Envelope<Sales.Order>' already gets in namespace 'Demo'",
            Message(diagnostic));
        AssertNoErrors(output);
        Assert.Contains("Demo.Envelope{Sales.Order}Serializer.g.cs", HintNames(result), StringComparer.Ordinal);
        Assert.DoesNotContain("Demo.Envelope{Billing.Order}Serializer.g.cs", HintNames(result), StringComparer.Ordinal);
    }

    [Fact]
    public void ZASZ011_ClosedGenericCollidingWithANonGenericType_IsReported()
    {
        var (output, result) = Run(MessagePackTypes, """
            using ZeroAlloc.Serialisation;

            [assembly: ZeroAllocSerializable(typeof(Demo.Envelope<Demo.Order>), SerializationFormat.MessagePack)]

            namespace Demo
            {
                [global::MessagePack.MessagePackObject]
                [ZeroAllocSerializable(SerializationFormat.MessagePack)]
                public sealed class EnvelopeOfOrder { [global::MessagePack.Key(0)] public int Id { get; set; } }
            }
            """);

        Assert.Equal(["ZASZ011"], Ids(result));
        AssertNoErrors(output);
    }

    [Fact]
    public void ZASZ001_OnAGenericDeclaration_StillReported_AndPointsAtTheAssemblyForm()
    {
        var (_, result) = Run("""
            using ZeroAlloc.Serialisation;
            namespace Demo;
            [ZeroAllocSerializable(SerializationFormat.MessagePack)]
            public sealed class Wrapper<T> { public T? Value { get; set; } }
            """);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("ZASZ001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("[assembly: ZeroAllocSerializable(typeof(Envelope<Order>)", Message(diagnostic), StringComparison.Ordinal);
    }

    [Fact]
    public void UnrelatedEdit_LeavesTheAssemblyFormStepsAndOutputsCached()
    {
        var declarations = CSharpSyntaxTree.ParseText(
            """
            using ZeroAlloc.Serialisation;
            [assembly: ZeroAllocSerializable(typeof(Demo.Envelope<Demo.Order>), SerializationFormat.MessagePack)]
            [assembly: ZeroAllocSerializable(typeof(Demo.Envelope<Demo.Order>), SerializationFormat.MessagePack)]
            [assembly: ZeroAllocSerializable(typeof(Demo.Pair<int, Demo.Order>), SerializationFormat.MessagePack)]
            """,
            path: "/src/Declarations.cs");
        var types = CSharpSyntaxTree.ParseText(MessagePackTypes, path: "/src/Types.cs");
        var unrelated = CSharpSyntaxTree.ParseText(
            "namespace Demo; public class Unrelated { public int M() => 1; }", path: "/src/Unrelated.cs");
        var compilation = CreateCompilation([declarations, types, unrelated]);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SerializerGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);

        var edited = compilation.ReplaceSyntaxTree(
            unrelated,
            unrelated.WithChangedText(SourceText.From(
                "namespace Demo; public class Unrelated { public int M() => 2; public int N() => 3; }")));
        driver = driver.RunGenerators(edited);
        var second = driver.GetRunResult().Results[0];

        string[] stepNames =
        [
            "TypeLevelNames", "AssemblyDeclarations", "ResolvedAssemblyDeclarations", "BoundAssemblyResults",
            "AssemblyModels", "AllAssemblyModels", "DispatcherModels",
        ];
        foreach (var name in stepNames)
        {
            Assert.True(second.TrackedSteps.TryGetValue(name, out var runSteps), $"Step '{name}' was not tracked.");
            AssertAllCachedOrUnchanged(name, runSteps);
        }

        foreach (var step in second.TrackedOutputSteps)
            AssertAllCachedOrUnchanged(step.Key, step.Value);

        // The cached duplicate still reports, bound to its own tree.
        var duplicate = Assert.Single(second.Diagnostics);
        Assert.Equal("ZASZ009", duplicate.Id);
        Assert.Same(declarations, duplicate.Location.SourceTree);
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

    private static string Message(Diagnostic diagnostic) => diagnostic.GetMessage(CultureInfo.InvariantCulture);

    private static List<string> Ids(GeneratorRunResult result) =>
        result.Diagnostics.Select(static d => d.Id).OrderBy(static i => i, StringComparer.Ordinal).ToList();

    private static List<string> HintNames(GeneratorRunResult result) =>
        result.GeneratedSources.Select(static s => s.HintName).OrderBy(static n => n, StringComparer.Ordinal).ToList();

    private static string Source(GeneratorRunResult result, string hintName) =>
        result.GeneratedSources.Single(s => string.Equals(s.HintName, hintName, StringComparison.Ordinal)).SourceText.ToString();

    private static void AssertNoErrors(Compilation output)
    {
        var errors = output.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .ToArray();
        Assert.True(errors.Length == 0, string.Join('\n', errors));
    }

    private static (Compilation Output, GeneratorRunResult Result) Run(params string[] sources)
    {
        var compilation = CreateCompilation(sources.Select(static s => CSharpSyntaxTree.ParseText(s)).ToArray());
        var driver = CSharpGeneratorDriver.Create(new SerializerGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var result = driver.GetRunResult().Results[0];
        Assert.Null(result.Exception);
        return (output, result);
    }

    private static CSharpCompilation CreateCompilation(SyntaxTree[] trees)
    {
        var references = new List<MetadataReference>(Basic.Reference.Assemblies.Net100.References.All)
        {
            MetadataReference.CreateFromFile(typeof(ZeroAllocSerializableAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(global::MessagePack.MessagePackSerializer).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(global::MessagePack.KeyAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(global::MemoryPack.MemoryPackSerializer).Assembly.Location),
            MetadataReference.CreateFromFile(
                typeof(global::Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
            MetadataReference.CreateFromFile(
                typeof(global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions).Assembly.Location),
        };

        return CSharpCompilation.Create(
            "TestAssembly",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }
}
