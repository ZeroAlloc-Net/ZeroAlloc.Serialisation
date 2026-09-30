using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace ZeroAlloc.Serialisation.Generator.Tests;

/// <summary>
/// ZeroAlloc.ValueObjects allows <c>[ValueObject]</c> on structs and classes, top-level or nested.
/// For every shape and every backend, these tests compile the generated code with the user's
/// source, load the assembly and round-trip values through the real serializer, including null
/// for reference types.
/// </summary>
public sealed class ValueObjectShapeRoundTripTests
{
    private const string ValueObjectStub = """
        namespace ZeroAlloc.ValueObjects
        {
            [System.AttributeUsage(System.AttributeTargets.Class | System.AttributeTargets.Struct)]
            public sealed class ValueObjectAttribute : System.Attribute { }
        }
        """;

    // Every shape the attribute allows: struct, record struct, class and record class, at the
    // top level and nested, including nested in nested, in a static class, in a record and in the
    // global namespace, with private and internal accessibility.
    private const string Shapes = """
        using ZeroAlloc.ValueObjects;

        namespace Demo
        {
            [ValueObject]
            public readonly partial struct StructId
            {
                public int Value { get; }
                public StructId(int value) => Value = value;
            }

            [ValueObject]
            public partial record struct RecordStructCode(string Value);

            [ValueObject]
            public sealed partial class ClassId
            {
                public int Value { get; }
                public ClassId(int value) => Value = value;
            }

            [ValueObject]
            public sealed partial record RecordName(string Value);

            [ValueObject]
            public partial record OpenRecordId(long Value);

            [ValueObject]
            internal sealed partial class InternalClassId
            {
                public int Value { get; }
                public InternalClassId(int value) => Value = value;
            }

            public partial class Outer
            {
                [ValueObject]
                public readonly partial struct NestedStructId
                {
                    public int Value { get; }
                    public NestedStructId(int value) => Value = value;
                }

                [ValueObject]
                public sealed partial record NestedName(string Value);

                [ValueObject]
                private sealed partial class PrivateId
                {
                    public int Value { get; }
                    public PrivateId(int value) => Value = value;
                }

                public readonly partial struct Middle
                {
                    [ValueObject]
                    public sealed partial record DeepName(string Value);

                    [ValueObject]
                    internal readonly partial record struct DeepStructId(long Value);
                }
            }

            public static partial class Holder
            {
                [ValueObject]
                public sealed partial class HeldId
                {
                    public int Value { get; }
                    public HeldId(int value) => Value = value;
                }
            }

            public partial record Envelope
            {
                [ValueObject]
                public partial record struct EnvelopeId(int Value);
            }
        }

        public partial class GlobalOuter
        {
            [ValueObject]
            public sealed partial class GlobalNestedId
            {
                public int Value { get; }
                public GlobalNestedId(int value) => Value = value;
            }
        }
        """;

    private const string SystemTextJsonProbe = """
        using System;
        using System.Collections.Generic;
        using System.Text.Json;
        using Demo;
        using ZeroAlloc.Serialisation.SystemTextJson;

        namespace Demo
        {
            public sealed class Dto
            {
                public ClassId? Id { get; set; }
                public RecordName? Name { get; set; }
                public Outer.Middle.DeepName? Deep { get; set; }
                public StructId? Struct { get; set; }
            }

            public partial class Outer
            {
                internal static void ProbePrivate(List<string> failures, JsonSerializerOptions options)
                {
                    Probe.RoundTrip(failures, new PrivateId(9), "9", options, static v => v.Value);
                    Probe.Null<PrivateId>(failures, options);
                }
            }

            public static class Probe
            {
                public static string Run()
                {
                    var failures = new List<string>();
                    // Default options: the [JsonConverter] attribute on each type.
                    // Registrar options: the converters and the resolver the registrar adds.
                    var registrar = new JsonSerializerOptions
                    {
                        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
                    }.AddZeroAllocValueObjectConverters();
                    foreach (var options in new[] { new JsonSerializerOptions(), registrar })
                    {
                        RoundTrip(failures, new StructId(1), "1", options, static v => v.Value);
                        RoundTrip(failures, new RecordStructCode("a"), "\"a\"", options, static v => v.Value);
                        RoundTrip(failures, new ClassId(2), "2", options, static v => v.Value);
                        RoundTrip(failures, new RecordName("b"), "\"b\"", options, static v => v.Value);
                        RoundTrip(failures, new OpenRecordId(3), "3", options, static v => v.Value);
                        RoundTrip(failures, new InternalClassId(4), "4", options, static v => v.Value);
                        RoundTrip(failures, new Outer.NestedStructId(5), "5", options, static v => v.Value);
                        RoundTrip(failures, new Outer.NestedName("c"), "\"c\"", options, static v => v.Value);
                        RoundTrip(failures, new Outer.Middle.DeepName("d"), "\"d\"", options, static v => v.Value);
                        RoundTrip(failures, new Outer.Middle.DeepStructId(6), "6", options, static v => v.Value);
                        RoundTrip(failures, new Holder.HeldId(7), "7", options, static v => v.Value);
                        RoundTrip(failures, new Envelope.EnvelopeId(8), "8", options, static v => v.Value);
                        RoundTrip(failures, new GlobalOuter.GlobalNestedId(10), "10", options, static v => v.Value);
                        Outer.ProbePrivate(failures, options);

                        Null<ClassId>(failures, options);
                        Null<RecordName>(failures, options);
                        Null<OpenRecordId>(failures, options);
                        Null<InternalClassId>(failures, options);
                        Null<Outer.NestedName>(failures, options);
                        Null<Outer.Middle.DeepName>(failures, options);
                        Null<Holder.HeldId>(failures, options);
                        Null<GlobalOuter.GlobalNestedId>(failures, options);

                        var full = new Dto { Id = new ClassId(1), Name = new RecordName("n"), Deep = new Outer.Middle.DeepName("d"), Struct = new StructId(2) };
                        var fullJson = JsonSerializer.Serialize(full, options);
                        Expect(failures, fullJson, "{\"Id\":1,\"Name\":\"n\",\"Deep\":\"d\",\"Struct\":2}");
                        var fullBack = JsonSerializer.Deserialize<Dto>(fullJson, options);
                        if (fullBack?.Id?.Value != 1 || fullBack.Name?.Value != "n" || fullBack.Deep?.Value != "d" || fullBack.Struct?.Value != 2)
                            failures.Add("stj dto round-trip: " + fullJson);

                        var empty = new Dto();
                        var emptyJson = JsonSerializer.Serialize(empty, options);
                        Expect(failures, emptyJson, "{\"Id\":null,\"Name\":null,\"Deep\":null,\"Struct\":null}");
                        var emptyBack = JsonSerializer.Deserialize<Dto>(emptyJson, options);
                        if (emptyBack is null || emptyBack.Id is not null || emptyBack.Name is not null || emptyBack.Deep is not null || emptyBack.Struct is not null)
                            failures.Add("stj null dto round-trip: " + emptyJson);
                    }
                    return string.Join("\n", failures);
                }

                internal static void RoundTrip<T>(List<string> failures, T value, string expected, JsonSerializerOptions options, Func<T, object> key)
                {
                    var json = JsonSerializer.Serialize(value, options);
                    Expect(failures, json, expected);
                    var back = JsonSerializer.Deserialize<T>(json, options);
                    if (back is null || !Equals(key(back), key(value)))
                        failures.Add($"stj round-trip {typeof(T).Name}: {json}");
                }

                internal static void Null<T>(List<string> failures, JsonSerializerOptions options) where T : class
                {
                    var json = JsonSerializer.Serialize<T?>(null, options);
                    Expect(failures, json, "null");
                    if (JsonSerializer.Deserialize<T?>("null", options) is not null)
                        failures.Add($"stj null {typeof(T).Name} did not read back as null");
                }

                private static void Expect(List<string> failures, string actual, string expected)
                {
                    if (!string.Equals(actual, expected, StringComparison.Ordinal))
                        failures.Add($"stj wire: expected {expected}, got {actual}");
                }
            }
        }
        """;

    private const string MessagePackProbe = """
        using System;
        using System.Collections.Generic;
        using MessagePack;
        using Demo;
        using ZeroAlloc.Serialisation.MessagePack;

        namespace Demo
        {
            [MessagePackObject]
            public sealed class Dto
            {
                [Key(0)] public ClassId? Id { get; set; }
                [Key(1)] public RecordName? Name { get; set; }
                [Key(2)] public Outer.Middle.DeepName? Deep { get; set; }
                [Key(3)] public StructId? Struct { get; set; }
            }

            public partial class Outer
            {
                internal static void ProbePrivate(List<string> failures, MessagePackSerializerOptions options)
                {
                    Probe.RoundTrip(failures, new PrivateId(9), "9", options, static v => v.Value);
                    Probe.Null<PrivateId>(failures, options);
                }
            }

            public static class Probe
            {
                public static string Run()
                {
                    var failures = new List<string>();
                    // Standard options: the [MessagePackFormatter] attribute on each type.
                    // Resolver options: the resolver AddZeroAllocValueObjectFormatters prepends.
                    var standard = MessagePackSerializerOptions.Standard;
                    foreach (var options in new[] { standard, standard.AddZeroAllocValueObjectFormatters() })
                    {
                        RoundTrip(failures, new StructId(1), "1", options, static v => v.Value);
                        RoundTrip(failures, new RecordStructCode("a"), "\"a\"", options, static v => v.Value);
                        RoundTrip(failures, new ClassId(2), "2", options, static v => v.Value);
                        RoundTrip(failures, new RecordName("b"), "\"b\"", options, static v => v.Value);
                        RoundTrip(failures, new OpenRecordId(3), "3", options, static v => v.Value);
                        RoundTrip(failures, new InternalClassId(4), "4", options, static v => v.Value);
                        RoundTrip(failures, new Outer.NestedStructId(5), "5", options, static v => v.Value);
                        RoundTrip(failures, new Outer.NestedName("c"), "\"c\"", options, static v => v.Value);
                        RoundTrip(failures, new Outer.Middle.DeepName("d"), "\"d\"", options, static v => v.Value);
                        RoundTrip(failures, new Outer.Middle.DeepStructId(6), "6", options, static v => v.Value);
                        RoundTrip(failures, new Holder.HeldId(7), "7", options, static v => v.Value);
                        RoundTrip(failures, new Envelope.EnvelopeId(8), "8", options, static v => v.Value);
                        RoundTrip(failures, new GlobalOuter.GlobalNestedId(10), "10", options, static v => v.Value);
                        Outer.ProbePrivate(failures, options);

                        Null<ClassId>(failures, options);
                        Null<RecordName>(failures, options);
                        Null<OpenRecordId>(failures, options);
                        Null<InternalClassId>(failures, options);
                        Null<Outer.NestedName>(failures, options);
                        Null<Outer.Middle.DeepName>(failures, options);
                        Null<Holder.HeldId>(failures, options);
                        Null<GlobalOuter.GlobalNestedId>(failures, options);

                        var full = new Dto { Id = new ClassId(1), Name = new RecordName("n"), Deep = new Outer.Middle.DeepName("d"), Struct = new StructId(2) };
                        var fullBytes = MessagePackSerializer.Serialize(full, options);
                        Expect(failures, MessagePackSerializer.ConvertToJson(fullBytes), "[1,\"n\",\"d\",2]");
                        var fullBack = MessagePackSerializer.Deserialize<Dto>(fullBytes, options);
                        if (fullBack?.Id?.Value != 1 || fullBack.Name?.Value != "n" || fullBack.Deep?.Value != "d" || fullBack.Struct?.Value != 2)
                            failures.Add("messagepack dto round-trip mismatch");

                        var emptyBytes = MessagePackSerializer.Serialize(new Dto(), options);
                        Expect(failures, MessagePackSerializer.ConvertToJson(emptyBytes), "[null,null,null,null]");
                        var emptyBack = MessagePackSerializer.Deserialize<Dto>(emptyBytes, options);
                        if (emptyBack is null || emptyBack.Id is not null || emptyBack.Name is not null || emptyBack.Deep is not null || emptyBack.Struct is not null)
                            failures.Add("messagepack null dto round-trip mismatch");
                    }
                    return string.Join("\n", failures);
                }

                internal static void RoundTrip<T>(List<string> failures, T value, string expected, MessagePackSerializerOptions options, Func<T, object> key)
                {
                    var bytes = MessagePackSerializer.Serialize(value, options);
                    Expect(failures, MessagePackSerializer.ConvertToJson(bytes), expected);
                    var back = MessagePackSerializer.Deserialize<T>(bytes, options);
                    if (back is null || !Equals(key(back), key(value)))
                        failures.Add($"messagepack round-trip {typeof(T).Name}");
                }

                internal static void Null<T>(List<string> failures, MessagePackSerializerOptions options) where T : class
                {
                    var bytes = MessagePackSerializer.Serialize<T?>(null, options);
                    Expect(failures, MessagePackSerializer.ConvertToJson(bytes), "null");
                    if (MessagePackSerializer.Deserialize<T?>(bytes, options) is not null)
                        failures.Add($"messagepack null {typeof(T).Name} did not read back as null");
                }

                private static void Expect(List<string> failures, string actual, string expected)
                {
                    if (!string.Equals(actual, expected, StringComparison.Ordinal))
                        failures.Add($"messagepack wire: expected {expected}, got {actual}");
                }
            }
        }
        """;

    private const string MemoryPackProbe = """
        using System;
        using System.Collections.Generic;
        using MemoryPack;
        using Demo;

        namespace Demo
        {
            public partial class Outer
            {
                internal static void ProbePrivate(List<string> failures)
                {
                    Probe.RoundTrip(failures, new PrivateId(9), Probe.Object(Probe.Int(9)), static v => v.Value);
                    Probe.Null<PrivateId>(failures);
                }
            }

            public static class Probe
            {
                public static string Run()
                {
                    var failures = new List<string>();
                    // Structs keep the bare underlying value. Reference types carry MemoryPack's
                    // object header, one member, so null has a distinct encoding.
                    RoundTrip(failures, new StructId(1), Int(1), static v => v.Value);
                    RoundTrip(failures, new RecordStructCode("a"), Str("a"), static v => v.Value);
                    RoundTrip(failures, new ClassId(2), Object(Int(2)), static v => v.Value);
                    RoundTrip(failures, new RecordName("b"), Object(Str("b")), static v => v.Value);
                    RoundTrip(failures, new OpenRecordId(3), Object(BitConverter.GetBytes(3L)), static v => v.Value);
                    RoundTrip(failures, new InternalClassId(4), Object(Int(4)), static v => v.Value);
                    RoundTrip(failures, new Outer.NestedStructId(5), Int(5), static v => v.Value);
                    RoundTrip(failures, new Outer.NestedName("c"), Object(Str("c")), static v => v.Value);
                    RoundTrip(failures, new Outer.Middle.DeepName("d"), Object(Str("d")), static v => v.Value);
                    RoundTrip(failures, new Outer.Middle.DeepStructId(6), BitConverter.GetBytes(6L), static v => v.Value);
                    RoundTrip(failures, new Holder.HeldId(7), Object(Int(7)), static v => v.Value);
                    RoundTrip(failures, new Envelope.EnvelopeId(8), Int(8), static v => v.Value);
                    RoundTrip(failures, new GlobalOuter.GlobalNestedId(10), Object(Int(10)), static v => v.Value);
                    Outer.ProbePrivate(failures);

                    Null<ClassId>(failures);
                    Null<RecordName>(failures);
                    Null<OpenRecordId>(failures);
                    Null<InternalClassId>(failures);
                    Null<Outer.NestedName>(failures);
                    Null<Outer.Middle.DeepName>(failures);
                    Null<Holder.HeldId>(failures);
                    Null<GlobalOuter.GlobalNestedId>(failures);

                    // A nullable member inside a container: the array formatter asks the
                    // registered formatter for every element, null included.
                    var items = new ClassId?[] { new ClassId(1), null, new ClassId(3) };
                    var back = MemoryPackSerializer.Deserialize<ClassId?[]>(MemoryPackSerializer.Serialize(items));
                    if (back is null || back.Length != 3 || back[0]?.Value != 1 || back[1] is not null || back[2]?.Value != 3)
                        failures.Add("memorypack nullable element round-trip mismatch");

                    return string.Join("\n", failures);
                }

                internal static byte[] Int(int value) => BitConverter.GetBytes(value);

                internal static byte[] Str(string value) => MemoryPackSerializer.Serialize(value);

                internal static byte[] Object(byte[] member)
                {
                    var result = new byte[member.Length + 1];
                    result[0] = 1;
                    member.CopyTo(result, 1);
                    return result;
                }

                internal static void RoundTrip<T>(List<string> failures, T value, byte[] expected, Func<T, object> key)
                {
                    var bytes = MemoryPackSerializer.Serialize(value);
                    if (!bytes.AsSpan().SequenceEqual(expected))
                        failures.Add($"memorypack wire {typeof(T).Name}: expected {Convert.ToHexString(expected)}, got {Convert.ToHexString(bytes)}");
                    var back = MemoryPackSerializer.Deserialize<T>(bytes);
                    if (back is null || !Equals(key(back), key(value)))
                        failures.Add($"memorypack round-trip {typeof(T).Name}");
                }

                internal static void Null<T>(List<string> failures) where T : class
                {
                    var bytes = MemoryPackSerializer.Serialize<T?>(null);
                    if (bytes.Length != 1 || bytes[0] != MemoryPackCode.NullObject)
                        failures.Add($"memorypack null {typeof(T).Name} wire: {Convert.ToHexString(bytes)}");
                    if (MemoryPackSerializer.Deserialize<T?>(bytes) is not null)
                        failures.Add($"memorypack null {typeof(T).Name} did not read back as null");
                }
            }
        }
        """;

    [Fact]
    public void SystemTextJson_EveryShape_CompilesAndRoundTrips() =>
        AssertProbePasses(SystemTextJsonProbe, Backend.SystemTextJson);

    [Fact]
    public void MessagePack_EveryShape_CompilesAndRoundTrips() =>
        AssertProbePasses(MessagePackProbe, Backend.MessagePack);

    [Fact]
    public void MemoryPack_EveryShape_CompilesAndRoundTrips() =>
        AssertProbePasses(MemoryPackProbe, Backend.MemoryPack);

    [Fact]
    public void AllBackendsTogether_EveryShape_Compiles()
    {
        var output = RunGenerator([Shapes], Backend.SystemTextJson | Backend.MessagePack | Backend.MemoryPack);
        AssertNoErrorsOrGeneratedWarnings(output);
    }

    [Flags]
    private enum Backend
    {
        SystemTextJson = 1,
        MessagePack = 2,
        MemoryPack = 4,
    }

    private static void AssertProbePasses(string probe, Backend backend)
    {
        var output = RunGenerator([Shapes, probe], backend);
        AssertNoErrorsOrGeneratedWarnings(output);

        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        Assert.True(emit.Success, string.Join('\n', emit.Diagnostics));
        stream.Position = 0;

        var assembly = new AssemblyLoadContext(output.AssemblyName, isCollectible: false).LoadFromStream(stream);
        var run = assembly.GetType("Demo.Probe")!.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)!;
        var failures = (string)run.Invoke(null, null)!;
        Assert.True(failures.Length == 0, failures);
    }

    private static void AssertNoErrorsOrGeneratedWarnings(Compilation output)
    {
        // Consumers build with warnings as errors, so generated code must not warn either.
        var problems = output.GetDiagnostics()
            .Where(static d => d.Severity == DiagnosticSeverity.Error
                || (d.Severity == DiagnosticSeverity.Warning
                    && d.Location.SourceTree?.FilePath.EndsWith(".g.cs", StringComparison.Ordinal) == true))
            .Select(static d => d.ToString())
            .ToArray();
        Assert.True(problems.Length == 0, string.Join('\n', problems));
    }

    private static Compilation RunGenerator(string[] sources, Backend backends)
    {
        var references = new List<MetadataReference>(Basic.Reference.Assemblies.Net100.References.All)
        {
            MetadataReference.CreateFromFile(typeof(ZeroAllocSerializableAttribute).Assembly.Location),
        };
        if (backends.HasFlag(Backend.SystemTextJson))
        {
            references.Add(MetadataReference.CreateFromFile(
                typeof(ZeroAlloc.Serialisation.SystemTextJson.SystemTextJsonSerializer<int>).Assembly.Location));
        }
        if (backends.HasFlag(Backend.MessagePack))
        {
            references.Add(MetadataReference.CreateFromFile(
                typeof(ZeroAlloc.Serialisation.MessagePack.MessagePackSerializer<int>).Assembly.Location));
            references.Add(MetadataReference.CreateFromFile(typeof(global::MessagePack.MessagePackSerializer).Assembly.Location));
            references.Add(MetadataReference.CreateFromFile(typeof(global::MessagePack.KeyAttribute).Assembly.Location));
        }
        if (backends.HasFlag(Backend.MemoryPack))
        {
            references.Add(MetadataReference.CreateFromFile(
                typeof(ZeroAlloc.Serialisation.MemoryPack.MemoryPackSerializer<int>).Assembly.Location));
            references.Add(MetadataReference.CreateFromFile(typeof(global::MemoryPack.MemoryPackSerializer).Assembly.Location));
        }

        var trees = sources.Append(ValueObjectStub)
            .Select(static s => CSharpSyntaxTree.ParseText(s, new CSharpParseOptions(LanguageVersion.Latest)))
            .ToArray();
        var compilation = CSharpCompilation.Create(
            "ValueObjectShapes_" + Guid.NewGuid().ToString("N"),
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));

        CSharpGeneratorDriver.Create(new SerializerGenerator())
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        Assert.Empty(diagnostics);
        return output;
    }
}
