using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace ZeroAlloc.Serialisation.Generator.Tests;

/// <summary>
/// #185: same-named nested types in one namespace get qualified generated names, only when they
/// collide, and the ZeroAllocGeneratedAccessibility MSBuild property makes the generated entry
/// points internal, so two assemblies that both use the generator compile together.
/// </summary>
public sealed class GeneratedNamesAndAccessibilityTests
{
    private const string ValueObjectStub = """
        namespace ZeroAlloc.ValueObjects
        {
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
            public sealed class ValueObjectAttribute : System.Attribute { }
        }
        """;

    private const string SameNamedNestedTypes = """
        using ZeroAlloc.Serialisation;
        namespace Demo
        {
            public class A
            {
                [global::MessagePack.MessagePackObject]
                [ZeroAllocSerializable(SerializationFormat.MessagePack)]
                public sealed class Inner { [global::MessagePack.Key(0)] public int Id { get; set; } }
            }

            public class B
            {
                [global::MessagePack.MessagePackObject]
                [ZeroAllocSerializable(SerializationFormat.MessagePack)]
                public sealed class Inner { [global::MessagePack.Key(0)] public int Id { get; set; } }
            }

            [global::MessagePack.MessagePackObject]
            [ZeroAllocSerializable(SerializationFormat.MessagePack)]
            public sealed class Unique { [global::MessagePack.Key(0)] public int Id { get; set; } }
        }
        """;

    // ── Same-named nested types ──────────────────────────────────────────────

    [Fact]
    public void SameNamedNestedTypes_InOneNamespace_GetQualifiedNames_AndCompile()
    {
        var (output, result) = Run(null, SameNamedNestedTypes);

        AssertNoErrors(output);
        Assert.Empty(result.Diagnostics);

        Assert.Contains("internal sealed class Demo_A_InnerSerializer : ISerializer<global::Demo.A.Inner>", Source(result, "Demo.A.InnerSerializer.g.cs"), StringComparison.Ordinal);
        Assert.Contains("internal sealed class Demo_B_InnerSerializer : ISerializer<global::Demo.B.Inner>", Source(result, "Demo.B.InnerSerializer.g.cs"), StringComparison.Ordinal);
        Assert.Contains("public static IServiceCollection AddDemo_A_InnerSerializer(", Source(result, "Demo.A.InnerSerializerExtensions.g.cs"), StringComparison.Ordinal);
        Assert.Contains("public static IServiceCollection AddDemo_B_InnerSerializer(", Source(result, "Demo.B.InnerSerializerExtensions.g.cs"), StringComparison.Ordinal);

        var dispatcher = Source(result, "SerializerDispatcher.g.cs");
        Assert.Contains("case global::Demo.A.Inner __e: new global::Demo.Demo_A_InnerSerializer()", dispatcher, StringComparison.Ordinal);
        Assert.Contains("if (type == typeof(global::Demo.B.Inner)) return new global::Demo.Demo_B_InnerSerializer()", dispatcher, StringComparison.Ordinal);

        // A type whose name collides with nothing keeps its simple name.
        Assert.Contains("internal sealed class UniqueSerializer", Source(result, "Demo.UniqueSerializer.g.cs"), StringComparison.Ordinal);
        Assert.Contains("AddUniqueSerializer(", Source(result, "Demo.UniqueSerializerExtensions.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TopLevelType_CollidingWithANestedType_BothGetQualifiedNames()
    {
        var (output, result) = Run(null, """
            using ZeroAlloc.Serialisation;
            namespace Demo
            {
                [global::MessagePack.MessagePackObject]
                [ZeroAllocSerializable(SerializationFormat.MessagePack)]
                public sealed class Inner { [global::MessagePack.Key(0)] public int Id { get; set; } }

                public class A
                {
                    [global::MessagePack.MessagePackObject]
                    [ZeroAllocSerializable(SerializationFormat.MessagePack)]
                    public sealed class Inner { [global::MessagePack.Key(0)] public int Id { get; set; } }
                }
            }
            """);

        AssertNoErrors(output);
        Assert.Contains("AddDemo_InnerSerializer(", Source(result, "Demo.InnerSerializerExtensions.g.cs"), StringComparison.Ordinal);
        Assert.Contains("AddDemo_A_InnerSerializer(", Source(result, "Demo.A.InnerSerializerExtensions.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void SameNamedTypes_InDifferentNamespaces_KeepTheirSimpleNames()
    {
        var (output, result) = Run(null, """
            using ZeroAlloc.Serialisation;
            namespace Sales { public class A { [global::MessagePack.MessagePackObject][ZeroAllocSerializable(SerializationFormat.MessagePack)] public sealed class Inner { } } }
            namespace Billing { public class A { [global::MessagePack.MessagePackObject][ZeroAllocSerializable(SerializationFormat.MessagePack)] public sealed class Inner { } } }
            """);

        AssertNoErrors(output);
        Assert.Contains("AddInnerSerializer(", Source(result, "Sales.A.InnerSerializerExtensions.g.cs"), StringComparison.Ordinal);
        Assert.Contains("AddInnerSerializer(", Source(result, "Billing.A.InnerSerializerExtensions.g.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void UnrelatedEdit_LeavesTheNamingAndAccessibilityStepsCached()
    {
        var types = CSharpSyntaxTree.ParseText(SameNamedNestedTypes, path: "/src/Types.cs");
        var compilation = CreateCompilation([types, CSharpSyntaxTree.ParseText(ValueObjectStub, path: "/src/Stub.cs")]);

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new SerializerGenerator().AsSourceGenerator()],
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        driver = driver.RunGenerators(compilation);

        var unrelated = CSharpSyntaxTree.ParseText("namespace Demo; public class Unrelated { }", path: "/src/Unrelated.cs");
        driver = driver.RunGenerators(compilation.AddSyntaxTrees(unrelated));
        var second = driver.GetRunResult().Results[0];

        foreach (var name in new[] { "TypeLevelCollisions", "QualifiedModels", "GeneratedAccessibility" })
        {
            Assert.True(second.TrackedSteps.TryGetValue(name, out var steps), $"Step '{name}' was not tracked.");
            foreach (var step in steps)
            {
                foreach (var (_, reason) in step.Outputs)
                {
                    Assert.True(
                        reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged,
                        $"Step '{name}' produced output with reason {reason} after an unrelated edit.");
                }
            }
        }
    }

    // ── ZeroAllocGeneratedAccessibility ──────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Public")]
    [InlineData("public")]
    public void Accessibility_UnsetOrPublic_EmitsPublicEntryPoints(string? value)
    {
        var (output, result) = Run(value, SameNamedNestedTypes, ValueObjectSource);

        AssertNoErrors(output);
        Assert.Empty(result.Diagnostics);
        Assert.Contains("public sealed partial class SerializerDispatcher : ISerializerDispatcher", Source(result, "SerializerDispatcher.g.cs"), StringComparison.Ordinal);
        Assert.Contains("public static partial class SerializerServiceCollectionExtensions", Source(result, "SerializerDispatcherExtensions.g.cs"), StringComparison.Ordinal);
        Assert.Contains("public static partial class SerializerServiceCollectionExtensions", Source(result, "Demo.UniqueSerializerExtensions.g.cs"), StringComparison.Ordinal);
        Assert.Contains("public static class ValueObjectJsonConvertersExtensions", Source(result, "ValueObjectJsonConvertersExtensions.g.cs"), StringComparison.Ordinal);
        Assert.Contains("public static class ValueObjectMessagePackFormattersExtensions", Source(result, "ValueObjectMessagePackResolverExtensions.g.cs"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Internal")]
    [InlineData("internal")]
    [InlineData("INTERNAL")]
    public void Accessibility_Internal_EmitsInternalEntryPoints(string value)
    {
        var (output, result) = Run(value, SameNamedNestedTypes, ValueObjectSource);

        AssertNoErrors(output);
        Assert.Empty(result.Diagnostics);
        Assert.Contains("internal sealed partial class SerializerDispatcher : ISerializerDispatcher", Source(result, "SerializerDispatcher.g.cs"), StringComparison.Ordinal);
        Assert.Contains("internal static partial class SerializerServiceCollectionExtensions", Source(result, "SerializerDispatcherExtensions.g.cs"), StringComparison.Ordinal);
        Assert.Contains("internal static partial class SerializerServiceCollectionExtensions", Source(result, "Demo.UniqueSerializerExtensions.g.cs"), StringComparison.Ordinal);
        Assert.Contains("internal static class ValueObjectJsonConvertersExtensions", Source(result, "ValueObjectJsonConvertersExtensions.g.cs"), StringComparison.Ordinal);
        Assert.Contains("internal static class ValueObjectMessagePackFormattersExtensions", Source(result, "ValueObjectMessagePackResolverExtensions.g.cs"), StringComparison.Ordinal);
        Assert.DoesNotContain(result.GeneratedSources, s => s.SourceText.ToString().Contains("public static partial class", StringComparison.Ordinal));
        Assert.DoesNotContain(result.GeneratedSources, s => s.SourceText.ToString().Contains("public sealed partial class", StringComparison.Ordinal));
    }

    [Fact]
    public void Accessibility_InvalidValue_ReportsZASZ012Once_AndFallsBackToPublic()
    {
        var (output, result) = Run("Private", SameNamedNestedTypes);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("ZASZ012", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(
            "MSBuild property 'ZeroAllocGeneratedAccessibility' has invalid value 'Private'; allowed values are 'Public' and 'Internal'",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
        Assert.Contains("public sealed partial class SerializerDispatcher", Source(result, "SerializerDispatcher.g.cs"), StringComparison.Ordinal);
        AssertNoErrors(output);
    }

    [Fact]
    public void TwoAssemblies_BothUsingTheGenerator_WithInternalAccessibility_CompileTogether()
    {
        var stub = StubReference();
        var (lib, _) = Run("Internal", [stub], LibSource);
        var libReference = EmitReference(lib, "Lib");

        var (app, _) = Run("Internal", [stub, libReference], AppSource);

        var problems = app.GetDiagnostics()
            .Where(static d => d.Severity >= DiagnosticSeverity.Warning)
            .Select(static d => d.ToString())
            .ToArray();
        Assert.True(problems.Length == 0, string.Join('\n', problems));
        Assert.True(app.Emit(System.IO.Stream.Null).Success);
    }

    [Fact]
    public void TwoAssemblies_BothUsingTheGenerator_WithPublicAccessibility_ClashOnTheDispatcher()
    {
        // The failure the property exists for: without it, App sees Lib's public global
        // SerializerDispatcher next to its own.
        var stub = StubReference();
        var (lib, _) = Run(null, [stub], LibSource);
        var libReference = EmitReference(lib, "Lib");

        var (app, _) = Run(null, [stub, libReference], AppSource);

        Assert.Contains(app.GetDiagnostics(), static d => string.Equals(d.Id, "CS0436", StringComparison.Ordinal));
    }

    private const string ValueObjectSource = """
        namespace Demo
        {
            [ZeroAlloc.ValueObjects.ValueObject]
            public readonly partial struct OrderId
            {
                public int Value { get; }
                public OrderId(int value) => Value = value;
            }
        }
        """;

    private const string LibSource = """
        using ZeroAlloc.Serialisation;
        [assembly: ZeroAllocSerializable(typeof(Shared.Envelope<int>), SerializationFormat.MessagePack)]
        namespace Shared
        {
            [global::MessagePack.MessagePackObject]
            [ZeroAllocSerializable(SerializationFormat.MessagePack)]
            public sealed class LibMessage { [global::MessagePack.Key(0)] public int Id { get; set; } }

            [global::MessagePack.MessagePackObject]
            public sealed class Envelope<T> { [global::MessagePack.Key(0)] public T? Body { get; set; } }

            [ZeroAlloc.ValueObjects.ValueObject]
            public readonly partial struct LibId
            {
                public int Value { get; }
                public LibId(int value) => Value = value;
            }

            public static class LibRegistration
            {
                public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddLib(
                    this Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                    => services.AddLibMessageSerializer().AddEnvelopeOfInt32Serializer().AddSerializerDispatcher();
            }
        }
        """;

    // Generates into the same namespaces as Lib: the global dispatcher, Shared's extension class,
    // and the value-object registrars, and uses its own generated entry points.
    private const string AppSource = """
        using Microsoft.Extensions.DependencyInjection;
        using Shared;
        using ZeroAlloc.Serialisation;
        using ZeroAlloc.Serialisation.SystemTextJson;
        [assembly: ZeroAllocSerializable(typeof(Shared.Envelope<string>), SerializationFormat.MessagePack)]
        namespace Shared
        {
            [global::MessagePack.MessagePackObject]
            [ZeroAllocSerializable(SerializationFormat.MessagePack)]
            public sealed class AppMessage { [global::MessagePack.Key(0)] public int Id { get; set; } }

            [ZeroAlloc.ValueObjects.ValueObject]
            public readonly partial struct AppId
            {
                public int Value { get; }
                public AppId(int value) => Value = value;
            }
        }
        namespace App
        {
            public static class Startup
            {
                public static IServiceCollection Configure(IServiceCollection services)
                {
                    services.AddLib();
                    services.AddAppMessageSerializer().AddEnvelopeOfStringSerializer().AddSerializerDispatcher();
                    _ = new SerializerDispatcher();
                    _ = new System.Text.Json.JsonSerializerOptions().AddZeroAllocValueObjectConverters();
                    return services;
                }
            }
        }
        """;

    private static MetadataReference EmitReference(Compilation compilation, string assemblyName)
    {
        using var stream = new System.IO.MemoryStream();
        var emit = compilation.WithAssemblyName(assemblyName).Emit(stream);
        Assert.True(emit.Success, string.Join('\n', emit.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

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

    private static (Compilation Output, GeneratorRunResult Result) Run(string? accessibility, params string[] sources) =>
        Run(accessibility, [], sources.Append(ValueObjectStub).ToArray());

    /// <summary>The [ValueObject] stub as its own assembly, so two test assemblies can share it.</summary>
    private static MetadataReference StubReference() =>
        EmitReference(CreateCompilation([CSharpSyntaxTree.ParseText(ValueObjectStub)]), "ValueObjectsStub");

    private static (Compilation Output, GeneratorRunResult Result) Run(
        string? accessibility, MetadataReference[] extraReferences, params string[] sources)
    {
        var trees = sources.Select(static s => CSharpSyntaxTree.ParseText(s)).ToArray();
        var compilation = CreateCompilation(trees).AddReferences(extraReferences);
        var driver = CSharpGeneratorDriver.Create(
                [new SerializerGenerator().AsSourceGenerator()],
                optionsProvider: new GlobalOptionsProvider(accessibility))
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
            MetadataReference.CreateFromFile(typeof(ZeroAlloc.Serialisation.SystemTextJson.SystemTextJsonSerializer<int>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ZeroAlloc.Serialisation.MessagePack.MessagePackSerializer<int>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(global::MessagePack.MessagePackSerializer).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(global::MessagePack.KeyAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(
                typeof(global::Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
            MetadataReference.CreateFromFile(
                typeof(global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions).Assembly.Location),
        };

        return CSharpCompilation.Create(
            "TestAssembly",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable)
                .WithSpecificDiagnosticOptions(SdkDefaultNoWarn));
    }

    // The .NET SDK's default NoWarn: reference unification notices for MessagePack's net9.0 build.
    private static readonly ImmutableDictionary<string, ReportDiagnostic> SdkDefaultNoWarn =
        ImmutableDictionary<string, ReportDiagnostic>.Empty
            .Add("CS1701", ReportDiagnostic.Suppress)
            .Add("CS1702", ReportDiagnostic.Suppress);

    /// <summary>Supplies <c>build_property.ZeroAllocGeneratedAccessibility</c> as MSBuild would.</summary>
    private sealed class GlobalOptionsProvider(string? accessibility) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(accessibility is null
            ? ImmutableDictionary<string, string>.Empty
            : ImmutableDictionary<string, string>.Empty.Add("build_property.ZeroAllocGeneratedAccessibility", accessibility));

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Options.Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Options.Empty;

        private sealed class Options(ImmutableDictionary<string, string> values) : AnalyzerConfigOptions
        {
            public static readonly Options Empty = new(ImmutableDictionary<string, string>.Empty);

            public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) =>
                values.TryGetValue(key, out value);
        }
    }
}
