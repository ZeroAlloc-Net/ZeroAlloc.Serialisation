using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;
using ZeroAlloc.Serialisation.Generator;

namespace ZeroAlloc.Serialisation.Generator.Tests;

public class SerializerGeneratorTests
{
    [Fact]
    public void Generator_NoAttribute_EmitsNothing()
    {
        var source = """
            namespace MyApp;
            public class PlainClass { }
            """;

        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new SerializerGenerator())
            .RunGenerators(compilation);

        var result = driver.GetRunResult();
        Assert.Empty(result.GeneratedTrees);
    }

    [Fact]
    public void Generator_MemoryPack_EmitsSerializerAndDiFiles()
    {
        var source = """
            using ZeroAlloc.Serialisation;

            namespace MyApp;

            [ZeroAllocSerializable(SerializationFormat.MemoryPack)]
            public class OrderEvent { }
            """;

        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new SerializerGenerator())
            .RunGenerators(compilation);

        var result = driver.GetRunResult();
        Assert.Empty(result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
        Assert.Contains(result.GeneratedTrees, t => t.FilePath.Contains("OrderEventSerializer.g.cs"));
        Assert.Contains(result.GeneratedTrees, t => t.FilePath.Contains("OrderEventSerializerExtensions.g.cs"));
    }

    [Fact]
    public void Generator_EmittedSerializer_ContainsMemoryPackCall()
    {
        var source = """
            using ZeroAlloc.Serialisation;

            namespace MyApp;

            [ZeroAllocSerializable(SerializationFormat.MemoryPack)]
            public class OrderEvent { }
            """;

        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new SerializerGenerator())
            .RunGenerators(compilation);

        var result = driver.GetRunResult();
        var serializerFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("OrderEventSerializer.g.cs"));

        Assert.NotNull(serializerFile);
        var text = serializerFile!.GetText().ToString();
        Assert.Contains("MemoryPackSerializer.Serialize", text);
        Assert.Contains("MemoryPackSerializer.Deserialize<", text);
        Assert.Contains("ISerializer<MyApp.OrderEvent>", text);
    }

    [Fact]
    public void Generator_EmittedDiExtension_ContainsAddMethod()
    {
        var source = """
            using ZeroAlloc.Serialisation;

            namespace MyApp;

            [ZeroAllocSerializable(SerializationFormat.MessagePack)]
            public class InvoiceEvent { }
            """;

        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new SerializerGenerator())
            .RunGenerators(compilation);

        var result = driver.GetRunResult();
        var diFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("InvoiceEventSerializerExtensions.g.cs"));

        Assert.NotNull(diFile);
        var text = diFile!.GetText().ToString();
        Assert.Contains("AddInvoiceEventSerializer", text);
        Assert.Contains("ISerializer<MyApp.InvoiceEvent>", text);
    }

    [Fact]
    public void Generator_MessagePack_EmitsDeserializeWithSequenceConversion()
    {
        var source = """
            using ZeroAlloc.Serialisation;

            namespace MyApp;

            [ZeroAllocSerializable(SerializationFormat.MessagePack)]
            public class PaymentEvent { }
            """;

        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new SerializerGenerator())
            .RunGenerators(compilation);

        var result = driver.GetRunResult();
        var serializerFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("PaymentEventSerializer.g.cs"));

        Assert.NotNull(serializerFile);
        var text = serializerFile!.GetText().ToString();
        Assert.Contains("MessagePackSerializer.Serialize", text);
        Assert.Contains("MessagePackSerializer.Deserialize<", text);
    }

    [Fact]
    public void Generator_EmittedDiExtension_UsesTryAddSingleton()
    {
        var source = """
            using ZeroAlloc.Serialisation;

            namespace MyApp;

            [ZeroAllocSerializable(SerializationFormat.MemoryPack)]
            public class OrderEvent { }
            """;

        var compilation = CreateCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new SerializerGenerator())
            .RunGenerators(compilation);

        var result = driver.GetRunResult();
        var diFile = result.GeneratedTrees
            .FirstOrDefault(t => t.FilePath.Contains("OrderEventSerializerExtensions.g.cs"));

        Assert.NotNull(diFile);
        var text = diFile!.GetText().ToString();
        Assert.Contains("TryAddSingleton", text);
        Assert.DoesNotContain("services.AddSingleton", text);
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        // Include the ZeroAlloc.Serialisation assembly so the attribute is resolvable
        var zeroAllocRef = MetadataReference.CreateFromFile(
            typeof(ZeroAlloc.Serialisation.ZeroAllocSerializableAttribute).Assembly.Location);

        var references = new List<MetadataReference>(Basic.Reference.Assemblies.Net100.References.All)
        {
            zeroAllocRef,
        };

        return CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
