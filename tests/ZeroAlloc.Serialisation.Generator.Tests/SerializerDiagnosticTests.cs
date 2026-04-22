using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace ZeroAlloc.Serialisation.Generator.Tests;

public sealed class SerializerDiagnosticTests
{
    [Fact]
    public void ZASZ001_OpenGenericType_ProducesError()
    {
        var source = """
            using ZeroAlloc.Serialisation;
            namespace Demo;
            [ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
            public sealed class Wrapper<T> { public T? Value { get; set; } }
            """;
        var (_, diagnostics) = GeneratorTestHost.Generate(source);
        Assert.Contains(diagnostics, d => d.Id == "ZASZ001" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ZASZ002_UnknownFormat_ProducesError()
    {
        // Invalid enum cast: SerializationFormat is an enum backed by int; passing a constant
        // like (SerializationFormat)999 is legal C# but generates no known format branch.
        var source = """
            using ZeroAlloc.Serialisation;
            namespace Demo;
            [ZeroAllocSerializable((SerializationFormat)999)]
            public sealed class Bad { public string V { get; set; } = ""; }
            """;
        var (_, diagnostics) = GeneratorTestHost.Generate(source);
        Assert.Contains(diagnostics, d => d.Id == "ZASZ002" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ZASZ003_MemoryPackWithoutMemoryPackable_ProducesWarning()
    {
        var source = """
            using ZeroAlloc.Serialisation;
            namespace Demo;
            [ZeroAllocSerializable(SerializationFormat.MemoryPack)]
            public sealed class MissingMpAttr { public string V { get; set; } = ""; }
            """;
        var (_, diagnostics) = GeneratorTestHost.Generate(source);
        Assert.Contains(diagnostics, d => d.Id == "ZASZ003" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void ZASZ003_MessagePackWithoutMessagePackObject_ProducesWarning()
    {
        var source = """
            using ZeroAlloc.Serialisation;
            namespace Demo;
            [ZeroAllocSerializable(SerializationFormat.MessagePack)]
            public sealed class MissingMsgpAttr { public string V { get; set; } = ""; }
            """;
        var (_, diagnostics) = GeneratorTestHost.Generate(source);
        Assert.Contains(diagnostics, d => d.Id == "ZASZ003" && d.Severity == DiagnosticSeverity.Warning);
    }

    [Fact]
    public void ZASZ003_SystemTextJsonWithoutAttribute_ProducesNoWarning()
    {
        var source = """
            using ZeroAlloc.Serialisation;
            namespace Demo;
            [ZeroAllocSerializable(SerializationFormat.SystemTextJson)]
            public sealed class Clean { public string V { get; set; } = ""; }
            """;
        var (_, diagnostics) = GeneratorTestHost.Generate(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "ZASZ003");
    }

    [Fact]
    public void ZASZ003_MemoryPackWithMemoryPackable_ProducesNoWarning()
    {
        var source = """
            using ZeroAlloc.Serialisation;
            namespace MemoryPack
            {
                [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
                public sealed class MemoryPackableAttribute : System.Attribute { }
            }
            namespace Demo;
            [global::MemoryPack.MemoryPackable]
            [ZeroAllocSerializable(SerializationFormat.MemoryPack)]
            public partial class Proper { public string V { get; set; } = ""; }
            """;
        var (_, diagnostics) = GeneratorTestHost.Generate(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "ZASZ003");
    }
}
