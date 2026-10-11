using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Serialisation.Generator.Tests;

// [ZeroAllocSerializable] allows structs. The emitted serializer must implement
// ISerializer<T> for a value type T, whose unconstrained T? return is plain T,
// not Nullable<T>. These tests compile the generated code together with the
// user source and require zero errors, so a signature mismatch such as CS0738
// fails here instead of in a consumer's build.
public sealed class StructSerializerCompilationTests
{
    [Fact]
    public void SystemTextJson_RecordStruct_GeneratedCodeCompiles()
    {
        // The context is written by hand so the test does not depend on running
        // the System.Text.Json source generator; the ZeroAlloc generator only
        // needs [JsonSerializable] on a JsonSerializerContext subclass.
        var source = """
            using System;
            using System.Text.Json;
            using System.Text.Json.Serialization;
            using System.Text.Json.Serialization.Metadata;
            using ZeroAlloc.Serialisation;
            namespace Demo;
            [ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
            public readonly record struct StjPoint(int X, string Label);
            [JsonSerializable(typeof(StjPoint))]
            internal sealed class StjPointContext : JsonSerializerContext
            {
                public StjPointContext() : base(null) { }
                public static StjPointContext Default { get; } = new();
                public JsonTypeInfo<StjPoint> StjPoint => throw new NotSupportedException();
                protected override JsonSerializerOptions? GeneratedSerializerOptions => null;
                public override JsonTypeInfo? GetTypeInfo(Type type) => null;
            }
            """;

        AssertCompilesWithoutErrors(source, "StjPoint");
    }

    [Fact]
    public void MemoryPack_Struct_GeneratedCodeCompiles()
    {
        var source = """
            using ZeroAlloc.Serialisation;
            namespace Demo;
            [MemoryPack.MemoryPackable]
            [ZeroAllocSerializable(SerializationFormat.MemoryPack)]
            public partial struct MemPoint
            {
                public int X { get; set; }
            }
            """;

        AssertCompilesWithoutErrors(source, "MemPoint");
    }

    [Fact]
    public void MessagePack_Struct_GeneratedCodeCompiles()
    {
        var source = """
            using ZeroAlloc.Serialisation;
            namespace Demo;
            [global::MessagePack.MessagePackObject]
            [ZeroAllocSerializable(SerializationFormat.MessagePack)]
            public struct MsgPoint
            {
                [global::MessagePack.Key(0)] public int X { get; set; }
            }
            """;

        AssertCompilesWithoutErrors(source, "MsgPoint");
    }

    [Fact]
    public void Struct_DeserializeReturnsPlainValueType_NotNullable()
    {
        var source = """
            using ZeroAlloc.Serialisation;
            namespace Demo;
            [MemoryPack.MemoryPackable]
            [ZeroAllocSerializable(SerializationFormat.MemoryPack)]
            public partial struct MemPoint
            {
                public int X { get; set; }
            }
            """;

        var (_, generated) = RunGenerator(source);
        var serializer = generated.Single(t => t.FilePath.EndsWith("MemPointSerializer.g.cs", System.StringComparison.Ordinal));
        var text = serializer.GetText().ToString();

        Assert.Contains("public global::Demo.MemPoint Deserialize(global::System.ReadOnlySpan<byte> buffer)", text, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Demo.MemPoint?", text, System.StringComparison.Ordinal);
    }

    // The generic Serialize<T>(IBufferWriter<byte>, T) reinterprets T with Unsafe.As where the framework has
    // it, and casts through object elsewhere, such as in a netstandard2.1 consumer. Both branches must compile
    // for struct and class targets.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dispatcher_GenericSerialize_CompilesWithAndWithoutUnsafe(bool frameworkHasUnsafe)
    {
        var source = """
            using ZeroAlloc.Serialisation;
            namespace Demo;
            [MemoryPack.MemoryPackable]
            [ZeroAllocSerializable(SerializationFormat.MemoryPack)]
            public partial struct MemPoint
            {
                public int X { get; set; }
            }
            [global::MessagePack.MessagePackObject]
            [ZeroAllocSerializable(SerializationFormat.MessagePack)]
            public class MsgPoint
            {
                [global::MessagePack.Key(0)] public int X { get; set; }
            }
            """;

        var (output, generated) = RunGenerator(source, new CSharpParseOptions(preprocessorSymbols: frameworkHasUnsafe ? new[] { "NETCOREAPP3_0_OR_GREATER" } : null));

        var dispatcher = generated.Single(t => t.FilePath.EndsWith("SerializerDispatcher.g.cs", System.StringComparison.Ordinal));
        var text = dispatcher.GetText().ToString();
        Assert.Contains("public void Serialize<T>(IBufferWriter<byte> writer, T value)", text, System.StringComparison.Ordinal);
        Assert.Contains("if (typeof(T) == typeof(global::Demo.MemPoint))", text, System.StringComparison.Ordinal);
        Assert.Contains("Unsafe.As<T, global::Demo.MemPoint>(ref value)", text, System.StringComparison.Ordinal);
        Assert.Contains("(global::Demo.MemPoint)(object)value!", text, System.StringComparison.Ordinal);

        var errors = output.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .ToArray();
        Assert.True(errors.Length == 0, string.Join('\n', errors));
    }

    private static void AssertCompilesWithoutErrors(string source, string typeName)
    {
        var (output, generated) = RunGenerator(source);

        Assert.Contains(generated, t => t.FilePath.EndsWith(typeName + "Serializer.g.cs", System.StringComparison.Ordinal));
        Assert.Contains(generated, t => t.FilePath.EndsWith("SerializerDispatcher.g.cs", System.StringComparison.Ordinal));

        var errors = output.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .Select(static d => d.ToString())
            .ToArray();
        Assert.True(errors.Length == 0, string.Join('\n', errors));
    }

    private static (Compilation Output, IReadOnlyList<SyntaxTree> Generated) RunGenerator(
        string source, CSharpParseOptions? parseOptions = null)
    {
        var references = new List<MetadataReference>(Basic.Reference.Assemblies.Net100.References.All)
        {
            MetadataReference.CreateFromFile(typeof(ZeroAllocSerializableAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(global::MemoryPack.MemoryPackSerializer).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(global::MessagePack.MessagePackSerializer).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(global::MessagePack.KeyAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(
                typeof(global::Microsoft.Extensions.DependencyInjection.IServiceCollection).Assembly.Location),
        };

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { CSharpSyntaxTree.ParseText(source, parseOptions) },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        CSharpGeneratorDriver.Create(new[] { new SerializerGenerator().AsSourceGenerator() }, parseOptions: parseOptions)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
        var generated = output.SyntaxTrees.Where(t => !ReferenceEquals(t, compilation.SyntaxTrees[0])).ToArray();
        return (output, generated);
    }
}
