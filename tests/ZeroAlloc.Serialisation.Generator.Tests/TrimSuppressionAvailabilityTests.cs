using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Serialisation.Generator.Tests;

/// <summary>
/// The generated MemoryPack and MessagePack serializers carry <c>[UnconditionalSuppressMessage]</c>
/// only where the consuming project can apply it. netstandard2.1 has no such attribute; the internal
/// copies inside packages such as MessagePack are inaccessible, and applying one is CS0122.
/// </summary>
public sealed class TrimSuppressionAvailabilityTests
{
    // netstandard2.1 has no ZeroAlloc.Serialisation reference here, so the attribute, the enum,
    // ISerializer<T> and MessagePack's attribute are declared in source, as the real ones are named.
    private const string Stubs = """
        namespace ZeroAlloc.Serialisation
        {
            public enum SerializationFormat { MemoryPack, MessagePack, SystemTextJson }

            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
            public sealed class ZeroAllocSerializableAttribute : System.Attribute
            {
                public ZeroAllocSerializableAttribute(SerializationFormat format) { }
            }

            public interface ISerializer<T>
            {
                void Serialize(System.Buffers.IBufferWriter<byte> writer, T value);
                T? Deserialize(System.ReadOnlySpan<byte> buffer);
            }
        }

        namespace MessagePack
        {
            [System.AttributeUsage(System.AttributeTargets.Class)]
            public sealed class MessagePackObjectAttribute : System.Attribute { }
        }

        namespace Demo
        {
            [MessagePack.MessagePackObject]
            [ZeroAlloc.Serialisation.ZeroAllocSerializable(ZeroAlloc.Serialisation.SerializationFormat.MessagePack)]
            public sealed class Order { }
        }
        """;

    private const string AttributeSource = """
        namespace System.Diagnostics.CodeAnalysis
        {
            [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]
            {0} sealed class UnconditionalSuppressMessageAttribute : Attribute
            {
                public UnconditionalSuppressMessageAttribute(string category, string checkId) { }
                public string? Justification { get; set; }
            }
        }
        """;

    [Fact]
    public void Net10_EmitsSuppressions()
    {
        var (serializer, _) = Run(Basic.Reference.Assemblies.Net100.References.All, extraReferences: [], extraSource: null);

        Assert.Contains("UnconditionalSuppressMessage", serializer, StringComparison.Ordinal);
    }

    [Fact]
    public void NetStandard21_WithOnlyAPackagesInternalCopy_EmitsNoSuppressions()
    {
        var package = CompileReference("SomePackage", AttributeSource.Replace("{0}", "internal", StringComparison.Ordinal));

        var (serializer, output) = Run(Basic.Reference.Assemblies.NetStandard21.References.All, [package], extraSource: null);

        Assert.DoesNotContain("UnconditionalSuppressMessage", serializer, StringComparison.Ordinal);
        Assert.DoesNotContain(output.GetDiagnostics(), static d => string.Equals(d.Id, "CS0122", StringComparison.Ordinal));
    }

    [Fact]
    public void NetStandard21_WithTheProjectsOwnPolyfill_EmitsSuppressions()
    {
        var package = CompileReference("SomePackage", AttributeSource.Replace("{0}", "internal", StringComparison.Ordinal));
        var polyfill = AttributeSource.Replace("{0}", "internal", StringComparison.Ordinal);

        var (serializer, output) = Run(Basic.Reference.Assemblies.NetStandard21.References.All, [package], polyfill);

        Assert.Contains("UnconditionalSuppressMessage", serializer, StringComparison.Ordinal);
        Assert.DoesNotContain(output.GetDiagnostics(), static d => string.Equals(d.Id, "CS0122", StringComparison.Ordinal));
    }

    private static MetadataReference CompileReference(string name, string source)
    {
        var compilation = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(source)],
            Basic.Reference.Assemblies.NetStandard21.References.All,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        return compilation.ToMetadataReference();
    }

    private static (string Serializer, Compilation Output) Run(
        IEnumerable<MetadataReference> framework, IEnumerable<MetadataReference> extraReferences, string? extraSource)
    {
        var trees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(Stubs) };
        if (extraSource is not null) trees.Add(CSharpSyntaxTree.ParseText(extraSource));

        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            trees,
            framework.Concat(extraReferences),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        var driver = CSharpGeneratorDriver.Create(new SerializerGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var serializer = driver.GetRunResult().Results[0].GeneratedSources
            .Single(s => string.Equals(s.HintName, "Demo.OrderSerializer.g.cs", StringComparison.Ordinal))
            .SourceText.ToString();
        return (serializer, output);
    }
}
